using System.Net.Http;

namespace KRTradToFRLoL.Translation;

public sealed record TranslationResult(string Text, string Source, bool Failed = false);

/// <summary>
/// Chaîne de traduction, du plus rapide au plus lent, sans jamais bloquer le pipeline :
///   glossaire local (0 ms) → cache (0 ms) → LLM streaming (timeout dur)
///   → NMT local M2M-100 si présent (timeout) → coréen brut marqué non traduit.
/// Seules les traductions LLM sont mises en cache (le NMT local est rapide et de
/// moindre qualité : on ne veut pas resservir ses sorties quand le LLM revient).
/// </summary>
public sealed class TranslationPipeline(
    Glossary glossary,
    TranslationCache cache,
    ITranslator? llm,
    ITranslator? localFallback,
    double llmTimeoutSeconds,
    double localTimeoutSeconds)
{
    public async Task<TranslationResult> TranslateAsync(string koreanText, Action<string>? onPartial, CancellationToken ct)
    {
        var fromGlossary = glossary.TryTranslate(koreanText);
        if (fromGlossary is not null)
            return new TranslationResult(fromGlossary, "glossaire");

        var fromCache = cache.Get(koreanText);
        if (fromCache is not null)
            return new TranslationResult(fromCache, "cache");

        if (llm is not null)
        {
            var fromLlm = await TryTranslatorAsync(llm, koreanText, onPartial, llmTimeoutSeconds, ct);
            if (fromLlm is not null)
            {
                cache.Put(koreanText, fromLlm);
                return new TranslationResult(fromLlm, "llm");
            }
        }

        if (localFallback is not null)
        {
            var fromLocal = await TryTranslatorAsync(localFallback, koreanText, onPartial: null, localTimeoutSeconds, ct);
            if (fromLocal is not null)
                return new TranslationResult(fromLocal, "local");
        }

        return new TranslationResult(koreanText, "brut", Failed: true);
    }

    private static async Task<string?> TryTranslatorAsync(
        ITranslator translator, string text, Action<string>? onPartial, double timeoutSeconds, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            return await translator.TranslateAsync(text, onPartial, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null; // timeout de cet étage → étage suivant
        }
        catch (HttpRequestException)
        {
            return null; // réseau → étage suivant
        }
    }
}
