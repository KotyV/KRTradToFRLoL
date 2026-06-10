using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace KRTradToFRLoL.Ocr;

/// <summary>
/// OCR coréen spécialisé : modèle de reconnaissance PaddleOCR (PP-OCR korean) en ONNX —
/// voir tools/export_korean_ocr.py. Nettement plus précis que l'OCR Windows sur le hangul
/// 12-18 px du chat (l'OCR générique hallucine des idéogrammes, constaté en test réel).
/// La détection de lignes est faite par projection horizontale : le chat LoL est un bloc
/// de lignes régulières, le modèle de détection généraliste est inutile ici.
/// L'image arrive déjà agrandie et passée en niveaux de gris contrastés (EnhanceForOcr).
/// </summary>
public sealed class PaddleKoreanOcrEngine : IOcrEngine, IDisposable
{
    private const int InkThreshold = 140;     // l'image est contrastée en amont : texte clair
    private const int MinLineHeight = 10;     // sous ~10 px (déjà ×2), ce n'est pas du texte
    private const int LineMergeGap = 3;

    private readonly InferenceSession _session;
    private readonly string[] _charset;
    private readonly int _inputHeight;
    private readonly int _maxWidth;
    private readonly Lock _runLock = new();

    private PaddleKoreanOcrEngine(InferenceSession session, string[] charset, int inputHeight, int maxWidth)
    {
        _session = session;
        _charset = charset;
        _inputHeight = inputHeight;
        _maxWidth = maxWidth;
    }

    public string Name => "PaddleOCR coréen (ONNX)";

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    /// <summary>Charge le moteur si les fichiers sont présents, sinon null (fallback OCR Windows).</summary>
    public static PaddleKoreanOcrEngine? TryCreate(string modelDirectory)
    {
        try
        {
            var onnxPath = Path.Combine(modelDirectory, "rec.onnx");
            var charsetPath = Path.Combine(modelDirectory, "charset.txt");
            var metaPath = Path.Combine(modelDirectory, "krtrad-ocr-meta.json");
            if (!File.Exists(onnxPath) || !File.Exists(charsetPath) || !File.Exists(metaPath))
                return null;

            using var metaDoc = JsonDocument.Parse(File.ReadAllText(metaPath));
            var meta = metaDoc.RootElement;
            var inputHeight = meta.GetProperty("inputHeight").GetInt32();
            var maxWidth = meta.GetProperty("maxWidth").GetInt32();
            var appendSpace = meta.GetProperty("appendSpace").GetBoolean();

            // Décodage CTC PaddleOCR : indice 0 = blank, puis le dictionnaire, puis l'espace.
            // ATTENTION : chaque ligne du fichier est un indice — ne JAMAIS filtrer/dédoublonner.
            var chars = File.ReadAllLines(charsetPath).ToList();
            if (appendSpace) chars.Add(" ");

            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 4, 2, 4),
            };
            return new PaddleKoreanOcrEngine(new InferenceSession(onnxPath, options), [.. chars], inputHeight, maxWidth);
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or IOException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    public Task<IReadOnlyList<string>> RecognizeLinesAsync(Bitmap image) =>
        Task.Run(() =>
        {
            lock (_runLock)
            {
                var results = new List<string>();
                foreach (var band in SegmentLines(image))
                {
                    using var strip = CropStrip(image, band);
                    var text = RecognizeStrip(strip);
                    if (text.Length > 0) results.Add(text);
                }
                return (IReadOnlyList<string>)results;
            }
        });

    /// <summary>Bandes verticales contenant de l'encre (projection horizontale des pixels clairs).</summary>
    internal static List<(int Top, int Height)> SegmentLines(Bitmap bmp)
    {
        var inkPerRow = CountInkPerRow(bmp);
        var minInk = Math.Max(2, bmp.Width / 200); // quelques pixels suffisent à marquer une ligne

        var bands = new List<(int Top, int Height)>();
        var start = -1;
        for (var y = 0; y <= inkPerRow.Length; y++)
        {
            var hasInk = y < inkPerRow.Length && inkPerRow[y] >= minInk;
            if (hasInk && start < 0)
            {
                start = y;
            }
            else if (!hasInk && start >= 0)
            {
                // fusionne avec la bande précédente si l'interligne est minuscule (accents/jamo)
                if (bands.Count > 0 && start - (bands[^1].Top + bands[^1].Height) <= LineMergeGap)
                    bands[^1] = (bands[^1].Top, y - bands[^1].Top);
                else
                    bands.Add((start, y - start));
                start = -1;
            }
        }

        bands.RemoveAll(b => b.Height < MinLineHeight);
        return bands;
    }

    private static unsafe int[] CountInkPerRow(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var counts = new int[bmp.Height];
            var ptr = (byte*)data.Scan0;
            for (var y = 0; y < bmp.Height; y++)
            {
                var row = ptr + y * data.Stride;
                var count = 0;
                for (var x = 0; x < bmp.Width; x++)
                {
                    if (row[x * 4] >= InkThreshold) count++; // image en gris : un canal suffit
                }
                counts[y] = count;
            }
            return counts;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private Bitmap CropStrip(Bitmap source, (int Top, int Height) band)
    {
        // marge verticale de 2 px + redimensionnement à la hauteur d'entrée du modèle
        var top = Math.Max(0, band.Top - 2);
        var height = Math.Min(source.Height - top, band.Height + 4);
        var scale = (double)_inputHeight / height;
        var width = Math.Clamp((int)(source.Width * scale), _inputHeight, _maxWidth);

        var strip = new Bitmap(width, _inputHeight, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(strip);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(source,
            new Rectangle(0, 0, width, _inputHeight),
            new Rectangle(0, top, source.Width, height),
            GraphicsUnit.Pixel);
        return strip;
    }

    private unsafe string RecognizeStrip(Bitmap strip)
    {
        // Normalisation PaddleOCR : (x/255 - 0,5) / 0,5, ordre CHW.
        var tensor = new DenseTensor<float>([1, 3, _inputHeight, strip.Width]);
        var data = strip.LockBits(new Rectangle(0, 0, strip.Width, strip.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var ptr = (byte*)data.Scan0;
            for (var y = 0; y < strip.Height; y++)
            {
                var row = ptr + y * data.Stride;
                for (var x = 0; x < strip.Width; x++)
                {
                    var p = row + x * 4;
                    tensor[0, 0, y, x] = (p[2] / 255f - 0.5f) / 0.5f;
                    tensor[0, 1, y, x] = (p[1] / 255f - 0.5f) / 0.5f;
                    tensor[0, 2, y, x] = (p[0] / 255f - 0.5f) / 0.5f;
                }
            }
        }
        finally
        {
            strip.UnlockBits(data);
        }

        var inputName = _session.InputMetadata.Keys.First();
        using var outputs = _session.Run([NamedOnnxValue.CreateFromTensor(inputName, tensor)]);
        var logits = outputs.First().AsTensor<float>(); // [1, T, classes]

        return CtcGreedyDecode(logits);
    }

    private string CtcGreedyDecode(Tensor<float> logits)
    {
        var steps = (int)logits.Dimensions[1];
        var classes = (int)logits.Dimensions[2];
        var sb = new StringBuilder();
        var previous = 0;

        for (var t = 0; t < steps; t++)
        {
            var best = 0;
            var bestScore = float.MinValue;
            for (var c = 0; c < classes; c++)
            {
                var score = logits[0, t, c];
                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }

            // CTC : 0 = blank ; on ignore les répétitions consécutives du même indice.
            if (best != 0 && best != previous && best - 1 < _charset.Length)
                sb.Append(_charset[best - 1]);
            previous = best;
        }
        return sb.ToString().Trim();
    }

    public void Dispose() => _session.Dispose();
}
