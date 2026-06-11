using KRTradToFRLoL.Translation;
using Xunit;

namespace KRTradToFRLoL.Tests;

public class GlossaryTests
{
    private static readonly Glossary Unit = new(new Dictionary<string, string>
    {
        ["ㅈㅈ"] = "go ff",
        ["미드 미아"] = "mid mia",
    });

    [Fact]
    public void Correspondance_exacte() => Assert.Equal("go ff", Unit.TryTranslate("ㅈㅈ"));

    [Fact]
    public void Normalisation_espaces_et_casse() => Assert.Equal("mid mia", Unit.TryTranslate(" 미드  미아 "));

    [Theory]
    [InlineData("ㅋㅋㅋㅋㅋ", "mdrr")]
    [InlineData("ㅋㅋ", "mdrr")]
    [InlineData("ㅠㅠ", "(pleure)")]
    public void Rires_et_pleurs_purs(string input, string expected) =>
        Assert.Equal(expected, Unit.TryTranslate(input));

    [Fact]
    public void Message_inconnu_renvoie_null_pour_le_llm() =>
        Assert.Null(Unit.TryTranslate("이 문장은 사전에 없다"));

    [Fact]
    public void Correspondance_partielle_non_appliquee() =>
        Assert.Null(Unit.TryTranslate("ㅈㅈ 치자 빨리")); // contient ㅈㅈ mais ≠ message entier
}

/// <summary>Tests d'intégration sur les vrais fichiers data/ embarqués.</summary>
public class GlossaryDataTests
{
    [Fact]
    public void Le_glossaire_embarque_est_riche_et_contient_les_termes_critiques()
    {
        var glossary = Glossary.LoadFromDataDir();

        Assert.True(glossary.Count >= 800, $"glossaire trop pauvre : {glossary.Count} entrées");
        Assert.NotNull(glossary.TryTranslate("ㅈㅈ"));
        Assert.NotNull(glossary.TryTranslate("서렌"));
        Assert.NotNull(glossary.TryTranslate("ㄱㄱ"));
    }

    [Fact]
    public void Le_system_prompt_active_le_cache_anthropic()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "system-prompt.txt");

        Assert.True(File.Exists(path), "data/system-prompt.txt manquant");
        var prompt = File.ReadAllText(path);
        // Minimum cacheable Haiku 4.5 = 4096 tokens ; ~12 000 caractères est notre plancher de sécurité.
        Assert.True(prompt.Length >= 12000, $"system prompt trop court pour le prompt caching : {prompt.Length} caractères");
        Assert.Contains("UNIQUEMENT", prompt, StringComparison.OrdinalIgnoreCase);
    }
}
