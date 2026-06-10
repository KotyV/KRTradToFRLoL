using KRTradToFRLoL.Parsing;
using Xunit;

namespace KRTradToFRLoL.Tests;

/// <summary>
/// Filtre structurel — cas tirés des captures réelles du SoloQ Challenge Corée
/// (docs/observations-captures.md) : clients anglais, français et coréen.
/// </summary>
public class ChatLineParserTests
{
    private static readonly ChampionNames Champions = new(
    [
        "Lucian", "Rek'Sai", "Maître Yi", "Ezreal", "Yuumi", "Lee Sin", "Vi", "Jax",
        "Kennen", "Nautilus", "Jhin", "Karma", "나피리", "요네", "사일러스", "르블랑",
    ]);

    private readonly ChatLineParser _parser = new(Champions);

    [Theory]
    [InlineData("16:47 [Team] back number (Lucian): 에반데", "Team", "Lucian", "에반데", "16:47")]
    [InlineData("16:53 [Team] 여자친구 율덕 (Rek'Sai): 유리하게 싸울수있는데", "Team", "Rek'Sai", "유리하게 싸울수있는데", "16:53")]
    [InlineData("[Équipe] Maître Yi (Maître Yi): 라인", "Team", "Maître Yi", "라인", "")]
    [InlineData("04:33 [Team] 가볼까요 (Ezreal): 빨리처졍~", "Team", "Ezreal", "빨리처졍~", "04:33")]
    [InlineData("04:48 [Team] 종종부인 (Yuumi): 럭스 e때매 죄송해요", "Team", "Yuumi", "럭스 e때매 죄송해요", "04:48")]
    [InlineData("[팀] 김민주 (나피리): 백", "Team", "나피리", "백", "")]
    [InlineData("08:47 [Team] 아삿추시러 (Ezreal): 서렌?", "Team", "Ezreal", "서렌?", "08:47")]
    [InlineData("08:00 [Team] 아삿추시러 (Ezreal): ㅔ네 ㅋㅋㅋ", "Team", "Ezreal", "ㅔ네 ㅋㅋㅋ", "08:00")]
    [InlineData("28:51 [Team] 미니맵 이제 못봄 (Karma): 상대바론인데", "Team", "Karma", "상대바론인데", "28:51")]
    public void Garde_les_vrais_messages_de_chat(string line, string channel, string champion, string text, string timestamp)
    {
        var msg = _parser.Parse(line);

        Assert.NotNull(msg);
        Assert.Equal(channel, msg.Channel);
        Assert.Equal(champion, msg.Champion);
        Assert.Equal(text, msg.Text);
        Assert.Equal(timestamp, msg.Timestamp);
    }

    [Fact]
    public void Conserve_le_pseudo_avec_espaces()
    {
        var msg = _parser.Parse("02:51 [Team] 운 없는 사람 (Lee Sin): 도와줘");

        Assert.NotNull(msg);
        Assert.Equal("운 없는 사람", msg.Author);
    }

    [Theory]
    // Pings/système client anglais — pseudos coréens mais rien à traduire
    [InlineData("16:46 Nautilus (Nautilus): KaiSa used Flash")]
    [InlineData("05:47 종종부인 (Yuumi): Wait For Yuumi Heal - 0s")]
    [InlineData("16:50 Nautilus (Nautilus) purchased Control Ward")]
    [InlineData("28:53 미니맵 이제 못봄 (Karma) purchased Mikael's Blessing")]
    [InlineData("04:28 아무도 너를 위로하지 않았다 (Jhin) has slain 가볼까요 (Ezreal) for a double kill!")]
    [InlineData("02:54 488호 (Vi) signals 운 없는 사람 - (Lee Sin) to fall back")]
    [InlineData("08:42 운 없는 사람 (Lee Sin): Darius Ghost")]
    [InlineData("07:33 종종부인 (Yuumi) Ward Quest Complete!")]
    [InlineData("16:52 Objective Bounties are falling off soon.")]
    // Client français
    [InlineData("04:24 Maître Yi (Maître Yi) indique que des ennemis ont disparu")]
    // Client coréen — pings « 님이 … 보냄 » sans deux-points
    [InlineData("몬스터 (르블랑) 님이 가고 있음")]
    [InlineData("못참겠어 (사일러스)님이 제어 와드 아이템을 구입했습니다.")]
    [InlineData("Adrian mateos (요네) 님이 첫 번째 포탑을 파괴했습니다!")]
    // Chat sans hangul = déjà lisible par le streamer
    [InlineData("02:33 [Team] 아삿추시러 (Ezreal): AN")]
    public void Rejette_pings_kills_et_systeme(string line) => Assert.Null(_parser.Parse(line));

    [Fact]
    public void Rejette_un_champion_inconnu_quand_la_liste_est_chargee() =>
        Assert.Null(_parser.Parse("[Team] joueur (Random Person): 한글 메시지"));

    [Fact]
    public void Sans_liste_de_champions_le_parsing_reste_permissif()
    {
        var permissive = new ChatLineParser(new ChampionNames());
        Assert.NotNull(permissive.Parse("[Team] joueur (Whatever): 한글"));
    }

    [Theory]
    [InlineData("안녕하세요", true)]
    [InlineData("ㅋㅋㅋ", true)]
    [InlineData("hello 123", false)]
    public void Detecte_le_hangul(string text, bool expected) =>
        Assert.Equal(expected, ChatLineParser.ContainsHangul(text));
}
