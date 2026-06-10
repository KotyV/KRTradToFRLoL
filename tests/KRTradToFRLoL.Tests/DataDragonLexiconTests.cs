using System.Text.Json;
using KRTradToFRLoL.Translation;
using Xunit;

namespace KRTradToFRLoL.Tests;

public class DataDragonLexiconTests
{
    private static JsonDocument Doc(string json) => JsonDocument.Parse(json);

    [Fact]
    public void Apparie_les_noms_par_cle_dentree()
    {
        using var ko = Doc("""{ "data": { "Garen": { "name": "가렌" }, "1001": { "name": "장화" } } }""");
        using var fr = Doc("""{ "data": { "Garen": { "name": "Garen" }, "1001": { "name": "Bottes" } } }""");

        var pairs = DataDragonLexicon.ExtractPairs(ko, fr).ToDictionary();

        Assert.Equal("Garen", pairs["가렌"]);
        Assert.Equal("Bottes", pairs["장화"]);
    }

    [Fact]
    public void Ignore_les_noms_identiques_et_les_entrees_incompletes()
    {
        using var ko = Doc("""{ "data": { "A": { "name": "Fiora" }, "B": { "name": "비에고" }, "C": { "name": "" } } }""");
        using var fr = Doc("""{ "data": { "A": { "name": "Fiora" }, "C": { "name": "Quelconque" } } }""");

        var pairs = DataDragonLexicon.ExtractPairs(ko, fr).ToList();

        Assert.Empty(pairs); // identique → ignoré ; clé absente côté fr → ignoré ; nom vide → ignoré
    }

    [Fact]
    public void Le_glossaire_verifie_garde_la_priorite_sur_le_lexique()
    {
        var glossary = new Glossary(new Dictionary<string, string> { ["점멸"] = "flash" });

        var added = glossary.TryAdd("점멸", "Saut éclair"); // nom officiel fr, mais l'entrée gamer existe

        Assert.False(added);
        Assert.Equal("flash", glossary.TryTranslate("점멸"));
        Assert.True(glossary.TryAdd("무한의 대검", "Lame d'infini")); // trou comblé par le lexique
        Assert.Equal("Lame d'infini", glossary.TryTranslate("무한의 대검"));
    }

    [Fact]
    public void La_prenormalisation_refuse_les_noms_multi_mots()
    {
        var prenorm = new Prenormalizer();

        Assert.True(prenorm.TryAdd("가렌", "Garen"));
        Assert.False(prenorm.TryAdd("무한의 대검", "Lame d'infini")); // multi-tokens → remplacement token entier impossible
        Assert.Equal("Garen 어디", prenorm.Apply("가렌 어디"));
    }
}
