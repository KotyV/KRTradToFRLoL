using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace KRTradToFRLoL.Translation.Local;

/// <summary>
/// Traduction 100 % locale via M2M-100 418M (licence MIT) exporté en ONNX —
/// voir tools/export_m2m100.py. Sert de filet quand le LLM est indisponible
/// (pas de clé, réseau coupé, timeout) : qualité moindre sur l'argot, d'où la
/// pré-normalisation argot → coréen standard appliquée en amont.
/// La génération est volontairement sérialisée (lock) et bridée en threads pour
/// ne pas affamer le jeu et OBS pendant un live.
/// </summary>
public sealed class M2M100Translator : ITranslator, IDisposable
{
    private const int MaxInputTokens = 96;
    private const int MaxNewTokens = 48;

    private readonly InferenceSession _encoder;
    private readonly InferenceSession _decoder;
    private readonly Tokenizer _spm;
    private readonly Dictionary<string, int> _vocab;
    private readonly Dictionary<int, string> _reverseVocab;
    private readonly Prenormalizer _prenormalizer;
    private readonly int _srcLangId;
    private readonly int _tgtLangId;
    private readonly int _eosId;
    private readonly int _unkId;
    private readonly Lock _generateLock = new();

    private M2M100Translator(InferenceSession encoder, InferenceSession decoder, Tokenizer spm,
        Dictionary<string, int> vocab, Prenormalizer prenormalizer, int srcLangId, int tgtLangId, int eosId, int unkId)
    {
        _encoder = encoder;
        _decoder = decoder;
        _spm = spm;
        _vocab = vocab;
        _reverseVocab = vocab.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.First().Key);
        _prenormalizer = prenormalizer;
        _srcLangId = srcLangId;
        _tgtLangId = tgtLangId;
        _eosId = eosId;
        _unkId = unkId;
    }

    /// <summary>Charge le modèle si les fichiers sont présents, sinon null (l'app continue sans).</summary>
    public static M2M100Translator? TryCreate(string modelDirectory, Prenormalizer prenormalizer)
    {
        try
        {
            var encoderPath = Path.Combine(modelDirectory, "encoder_model.onnx");
            var decoderPath = Path.Combine(modelDirectory, "decoder_model.onnx");
            var spmPath = Path.Combine(modelDirectory, "sentencepiece.bpe.model");
            var vocabPath = Path.Combine(modelDirectory, "vocab.json");
            var metaPath = Path.Combine(modelDirectory, "krtrad-meta.json");
            if (!File.Exists(encoderPath) || !File.Exists(decoderPath) || !File.Exists(spmPath)
                || !File.Exists(vocabPath) || !File.Exists(metaPath))
                return null;

            using var metaDoc = JsonDocument.Parse(File.ReadAllText(metaPath));
            var meta = metaDoc.RootElement;
            var srcLangId = meta.GetProperty("srcLangId").GetInt32();
            var tgtLangId = meta.GetProperty("tgtLangId").GetInt32();
            var eosId = meta.GetProperty("eosId").GetInt32();
            var unkId = meta.GetProperty("unkId").GetInt32();

            var vocab = new Dictionary<string, int>(StringComparer.Ordinal);
            using (var vocabDoc = JsonDocument.Parse(File.ReadAllText(vocabPath)))
            {
                foreach (var prop in vocabDoc.RootElement.EnumerateObject())
                    vocab[prop.Name] = prop.Value.GetInt32();
            }

            using var spmStream = File.OpenRead(spmPath);
            var spm = SentencePieceTokenizer.Create(spmStream, addBeginningOfSentence: false, addEndOfSentence: false);

            // Peu de threads : le jeu et OBS ont la priorité CPU pendant un live.
            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 4, 2, 4),
            };
            var encoder = new InferenceSession(encoderPath, options);
            var decoder = new InferenceSession(decoderPath, options);

            return new M2M100Translator(encoder, decoder, spm, vocab, prenormalizer, srcLangId, tgtLangId, eosId, unkId);
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or IOException or JsonException or InvalidOperationException or ArgumentException)
        {
            return null; // modèle absent/corrompu → traduction locale désactivée, l'app vit sans
        }
    }

    public Task<string?> TranslateAsync(string koreanText, Action<string>? onPartial, CancellationToken ct) =>
        Task.Run(() =>
        {
            lock (_generateLock)
            {
                return Generate(_prenormalizer.Apply(koreanText), ct);
            }
        }, ct);

    private string? Generate(string text, CancellationToken ct)
    {
        var inputIds = Encode(text);
        if (inputIds.Length < 2) return null;

        // Encodeur
        var seqLen = inputIds.Length;
        var inputTensor = new DenseTensor<long>(inputIds.Select(i => (long)i).ToArray(), [1, seqLen]);
        var maskTensor = new DenseTensor<long>(Enumerable.Repeat(1L, seqLen).ToArray(), [1, seqLen]);

        using var encoderResult = _encoder.Run(
        [
            NamedOnnxValue.CreateFromTensor("input_ids", inputTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", maskTensor),
        ]);
        var hiddenStates = encoderResult.First().AsTensor<float>().ToDenseTensor();

        // Décodeur greedy : [eos, __fr__] puis un token à la fois.
        var decoderIds = new List<long> { _eosId, _tgtLangId };
        var output = new List<int>();
        for (var step = 0; step < MaxNewTokens; step++)
        {
            ct.ThrowIfCancellationRequested();

            var decTensor = new DenseTensor<long>(decoderIds.ToArray(), [1, decoderIds.Count]);
            using var decoderResult = _decoder.Run(
            [
                NamedOnnxValue.CreateFromTensor("input_ids", decTensor),
                NamedOnnxValue.CreateFromTensor("encoder_attention_mask", maskTensor),
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states", hiddenStates),
            ]);

            var logits = decoderResult.First().AsTensor<float>();
            var next = ArgmaxLastPosition(logits, decoderIds.Count);
            if (next == _eosId) break;
            decoderIds.Add(next);
            output.Add(next);
        }

        var translation = Decode(output);
        return translation.Length > 0 ? translation : null;
    }

    private static int ArgmaxLastPosition(Tensor<float> logits, int positions)
    {
        var vocabSize = (int)logits.Dimensions[2];
        var last = positions - 1;
        var best = 0;
        var bestScore = float.MinValue;
        for (var v = 0; v < vocabSize; v++)
        {
            var score = logits[0, last, v];
            if (score > bestScore)
            {
                bestScore = score;
                best = v;
            }
        }
        return best;
    }

    private int[] Encode(string text)
    {
        var pieces = _spm.EncodeToTokens(text, out _);
        var ids = new List<int>(pieces.Count + 2) { _srcLangId };
        foreach (var piece in pieces.Take(MaxInputTokens))
            ids.Add(_vocab.GetValueOrDefault(piece.Value, _unkId));
        ids.Add(_eosId);
        return [.. ids];
    }

    private string Decode(IEnumerable<int> ids)
    {
        var sb = new StringBuilder();
        foreach (var id in ids)
        {
            if (!_reverseVocab.TryGetValue(id, out var piece)) continue;
            if (piece.StartsWith("__", StringComparison.Ordinal) ||
                piece is "<s>" or "</s>" or "<pad>" or "<unk>") continue;
            sb.Append(piece);
        }
        return sb.ToString().Replace('▁', ' ').Trim();
    }

    public void Dispose()
    {
        _encoder.Dispose();
        _decoder.Dispose();
    }
}
