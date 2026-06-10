using KRTradToFRLoL.Translation;
using Xunit;

namespace KRTradToFRLoL.Tests;

public class TranslationPipelineTests
{
    private sealed class FakeTranslator(string? result, int delayMs = 0, bool throwNetwork = false) : ITranslator
    {
        public int Calls { get; private set; }

        public async Task<string?> TranslateAsync(string koreanText, Action<string>? onPartial, CancellationToken ct)
        {
            Calls++;
            if (throwNetwork) throw new System.Net.Http.HttpRequestException("réseau coupé");
            if (delayMs > 0) await Task.Delay(delayMs, ct);
            return result;
        }
    }

    private static readonly Glossary EmptyGlossary = new(new Dictionary<string, string>());
    private static readonly Glossary SmallGlossary = new(new Dictionary<string, string> { ["ㅈㅈ"] = "go ff" });

    [Fact]
    public async Task Le_glossaire_court_circuite_le_llm()
    {
        var llm = new FakeTranslator("jamais appelé");
        var pipeline = new TranslationPipeline(SmallGlossary, new TranslationCache(), llm, null, 1, 1);

        var result = await pipeline.TranslateAsync("ㅈㅈ", null, CancellationToken.None);

        Assert.Equal(("go ff", "glossaire"), (result.Text, result.Source));
        Assert.Equal(0, llm.Calls);
    }

    [Fact]
    public async Task Le_llm_traduit_et_alimente_le_cache()
    {
        var cache = new TranslationCache();
        var pipeline = new TranslationPipeline(EmptyGlossary, cache, new FakeTranslator("trad llm"), null, 1, 1);

        var first = await pipeline.TranslateAsync("문장", null, CancellationToken.None);
        var second = await pipeline.TranslateAsync("문장", null, CancellationToken.None);

        Assert.Equal("llm", first.Source);
        Assert.Equal(("trad llm", "cache"), (second.Text, second.Source));
    }

    [Fact]
    public async Task Timeout_llm_bascule_sur_le_local()
    {
        var llm = new FakeTranslator("trop tard", delayMs: 2000);
        var local = new FakeTranslator("trad locale");
        var pipeline = new TranslationPipeline(EmptyGlossary, new TranslationCache(), llm, local, llmTimeoutSeconds: 0.1, localTimeoutSeconds: 1);

        var result = await pipeline.TranslateAsync("문장", null, CancellationToken.None);

        Assert.Equal(("trad locale", "local", false), (result.Text, result.Source, result.Failed));
    }

    [Fact]
    public async Task Panne_reseau_llm_bascule_sur_le_local()
    {
        var pipeline = new TranslationPipeline(EmptyGlossary, new TranslationCache(),
            new FakeTranslator(null, throwNetwork: true), new FakeTranslator("trad locale"), 1, 1);

        var result = await pipeline.TranslateAsync("문장", null, CancellationToken.None);

        Assert.Equal("local", result.Source);
    }

    [Fact]
    public async Task Le_local_nest_pas_mis_en_cache()
    {
        var cache = new TranslationCache();
        var pipeline = new TranslationPipeline(EmptyGlossary, cache, null, new FakeTranslator("trad locale"), 1, 1);

        await pipeline.TranslateAsync("문장", null, CancellationToken.None);

        Assert.Null(cache.Get("문장"));
    }

    [Fact]
    public async Task Tout_echoue_renvoie_le_brut_marque_non_traduit()
    {
        var pipeline = new TranslationPipeline(EmptyGlossary, new TranslationCache(),
            new FakeTranslator(null), new FakeTranslator(null), 1, 1);

        var result = await pipeline.TranslateAsync("문장", null, CancellationToken.None);

        Assert.Equal(("문장", "brut", true), (result.Text, result.Source, result.Failed));
    }

    [Fact]
    public async Task Sans_aucun_traducteur_renvoie_le_brut()
    {
        var pipeline = new TranslationPipeline(EmptyGlossary, new TranslationCache(), null, null, 1, 1);

        var result = await pipeline.TranslateAsync("문장", null, CancellationToken.None);

        Assert.True(result.Failed);
    }
}
