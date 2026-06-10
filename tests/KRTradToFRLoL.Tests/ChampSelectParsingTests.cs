using KRTradToFRLoL.Parsing;
using Xunit;

namespace KRTradToFRLoL.Tests;

/// <summary>
/// Chat du champ select (capture 14) : « Pseudo : message », sans tag de canal ni
/// parenthèse champion ; en ranked les coéquipiers sont anonymisés en camps de jungle.
/// </summary>
public class ChampSelectParsingTests
{
    private readonly ChatLineParser _parser = new(new ChampionNames(["Lee Sin", "Yone"]));

    [Theory]
    [InlineData("Krug : 아 메튜렁 이새기", "Krug", "아 메튜렁 이새기")]
    [InlineData("Krug : 진짜", "Krug", "진짜")]
    [InlineData("Krug : 연승치니까 주포절대안주네 4판연속", "Krug", "연승치니까 주포절대안주네 4판연속")]
    [InlineData("Murk Wolf: 미드 주세요", "Murk Wolf", "미드 주세요")]
    public void Parse_le_chat_du_champ_select(string line, string author, string text)
    {
        var msg = _parser.Parse(line);

        Assert.NotNull(msg);
        Assert.Equal("Lobby", msg.Channel);
        Assert.Equal(author, msg.Author);
        Assert.Equal(text, msg.Text);
        Assert.Equal(author, msg.SpeakerKey); // pas de champion → le pseudo identifie l'émetteur
    }

    [Theory]
    [InlineData("Krug : gl hf")]                                  // pas de hangul → déjà lisible
    [InlineData("Kennen rapide")]                                 // saisie en cours, pas de ':'
    [InlineData("16:46 Nautilus (Nautilus): KaiSa used Flash")]   // format in-game intact
    [InlineData("몬스터 (르블랑) 님이 가고 있음")]                    // système coréen in-game
    public void Ne_capte_pas_les_autres_formats(string line) => Assert.Null(_parser.Parse(line));

    [Theory]
    // Bruit d'OCR réel (test live sur VOD) : annonces anglaises massacrées + hangul halluciné.
    // L'ancien filtre laissait passer la 1re → garbage affiché dans l'overlay.
    [InlineData("1003 Tbe ,: •Qui «;•zn has sian tbe Cberntech Drake!")]
    [InlineData("누 : zn has sian tbe Cberntech 누rake!")]
    [InlineData("|055 Tcamj : Mo(Y5 시")]
    [InlineData("I IOI Tcamj : 10(YS go ahne farm")]
    public void Rejette_le_bruit_ocr_des_annonces_systeme(string line) => Assert.Null(_parser.Parse(line));

    [Fact]
    public void Exige_un_message_majoritairement_coreen()
    {
        Assert.True(ChatLineParser.IsMostlyHangul("아 메튜렁 이새기"));
        Assert.True(ChatLineParser.IsMostlyHangul("ㅋㅋ"));
        Assert.True(ChatLineParser.IsMostlyHangul("럭스 e때매 죄송해요"));
        Assert.False(ChatLineParser.IsMostlyHangul("zn has sian tbe Cberntech 누rake!"));
        Assert.False(ChatLineParser.IsMostlyHangul("go alone farm"));
    }

    [Fact]
    public void Le_format_in_game_reste_prioritaire()
    {
        var msg = _parser.Parse("[Team] 운 없는 사람 (Lee Sin): 도와줘");
        Assert.NotNull(msg);
        Assert.Equal("Team", msg.Channel);
        Assert.Equal("Lee Sin", msg.SpeakerKey);
    }
}
