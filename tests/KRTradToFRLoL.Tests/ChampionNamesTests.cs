using KRTradToFRLoL.Parsing;
using Xunit;

namespace KRTradToFRLoL.Tests;

public class ChampionNamesTests
{
    private static readonly ChampionNames Names = new(["Lee Sin", "Maître Yi", "Rek'Sai", "나피리", "Ezreal"]);

    [Theory]
    [InlineData("Lee Sin")]
    [InlineData("lee sin")]      // casse
    [InlineData("Rek'Sai")]
    [InlineData("나피리")]
    public void Reconnait_les_champions_connus(string token) => Assert.True(Names.IsChampion(token));

    [Fact]
    public void Tolere_une_erreur_docr() => Assert.True(Names.IsChampion("Ezreal".Replace('l', '1'))); // Ezrea1

    [Fact]
    public void Rejette_un_token_eloigne() => Assert.False(Names.IsChampion("Random Person"));

    [Fact]
    public void Liste_vide_accepte_tout()
    {
        var empty = new ChampionNames();
        Assert.True(empty.IsChampion("N'importe quoi"));
    }
}

public class LevenshteinTests
{
    [Theory]
    [InlineData("abc", "abc", 0)]
    [InlineData("abc", "abd", 1)]
    [InlineData("abc", "", 3)]
    [InlineData("에반데", "예반데", 1)]
    public void Distance(string a, string b, int expected) =>
        Assert.Equal(expected, Levenshtein.Distance(a, b));

    [Fact]
    public void Similarite_normalisee()
    {
        Assert.Equal(1.0, Levenshtein.Similarity("abc", "abc"));
        Assert.Equal(0.0, Levenshtein.Similarity("abc", "xyz"), 3);
    }
}
