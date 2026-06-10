using KRTradToFRLoL.Translation;
using Xunit;

namespace KRTradToFRLoL.Tests;

public class PrenormalizerTests
{
    private static readonly Prenormalizer Prenorm = new(new Dictionary<string, string>
    {
        ["갱"] = "기습 공격",
        ["서렌"] = "항복",
    });

    [Fact]
    public void Remplace_un_token_entier() =>
        Assert.Equal("미드 기습 공격 와", Prenorm.Apply("미드 갱 와"));

    [Fact]
    public void Ne_touche_pas_aux_tokens_partiels() =>
        Assert.Equal("갱플랭크 온다", Prenorm.Apply("갱플랭크 온다")); // 갱 ≠ 갱플랭크 (Gangplank)

    [Fact]
    public void Table_vide_renvoie_le_texte_intact()
    {
        var empty = new Prenormalizer();
        Assert.Equal("미드 갱 와", empty.Apply("미드 갱 와"));
    }

    [Fact]
    public void La_table_embarquee_se_charge()
    {
        var prenorm = Prenormalizer.LoadFromDataDir();
        Assert.True(prenorm.Count > 0, "data/prenorm.json absent ou vide");
        Assert.Equal("항복", prenorm.Apply("서렌"));
    }
}
