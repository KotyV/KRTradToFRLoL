using KRTradToFRLoL.Parsing;
using Xunit;

namespace KRTradToFRLoL.Tests;

/// <summary>Recollage des messages longs repliés sur plusieurs lignes (capture 13).</summary>
public class ChatFrameAssemblerTests
{
    private static readonly ChatFrameAssembler Assembler =
        new(new ChatLineParser(new ChampionNames(["Yunara", "Yone", "Sylas", "Lee Sin", "Hwei", "Diana"])));

    [Fact]
    public void Fusionne_la_ligne_de_continuation_avec_le_message_precedent()
    {
        var messages = Assembler.Assemble(
        [
            "24:55 [Team] Farm9 (Yunara): 본인이쓰레기처럼못한걸",
            "미안합니다나와아하는게 정상아닌가요?",
        ]);

        var msg = Assert.Single(messages);
        Assert.Equal("본인이쓰레기처럼못한걸 미안합니다나와아하는게 정상아닌가요?", msg.Text);
    }

    [Fact]
    public void Gere_un_repli_sur_trois_lignes()
    {
        var messages = Assembler.Assemble(
        [
            "[Team] sinano (Yone): 첫번째",
            "두번째",
            "세번째",
        ]);

        var msg = Assert.Single(messages);
        Assert.Equal("첫번째 두번째 세번째", msg.Text);
    }

    [Fact]
    public void Une_ligne_orpheline_sans_message_precedent_est_ignoree()
    {
        var messages = Assembler.Assemble(
        [
            "미안합니다나와아하는게 정상아닌가요?", // en-tête défilé hors zone : irrécupérable
            "[Team] sinano (Yone): 네넹",
        ]);

        var msg = Assert.Single(messages);
        Assert.Equal("네넹", msg.Text);
    }

    [Fact]
    public void Un_fragment_systeme_coreen_nest_pas_fusionne()
    {
        var messages = Assembler.Assemble(
        [
            "[Team] sinano (Yone): 네넹",
            "몬스터 (르블랑) 님이 후퇴 신호를 보냄",
        ]);

        var msg = Assert.Single(messages);
        Assert.Equal("네넹", msg.Text);
    }

    [Fact]
    public void Une_ligne_systeme_intercalee_coupe_la_continuation()
    {
        var messages = Assembler.Assemble(
        [
            "[Team] sinano (Yone): 네넹",
            "25:01 도구연구협회장 (Sylas) purchased Control Ward",
            "정상아닌가요?", // ne suit pas immédiatement un message parsé → ignorée
        ]);

        var msg = Assert.Single(messages);
        Assert.Equal("네넹", msg.Text);
    }

    [Fact]
    public void Une_ligne_systeme_anglaise_avec_pseudo_coreen_nest_pas_fusionnee()
    {
        // Bug réel attrapé par les tests : « (Sylas) purchased Control Ward » contient du
        // hangul (le pseudo) mais c'est une nouvelle entrée, pas une continuation.
        var messages = Assembler.Assemble(
        [
            "[Team] sinano (Yone): 네넹",
            "도구연구협회장 (Sylas) purchased Control Ward",
        ]);

        var msg = Assert.Single(messages);
        Assert.Equal("네넹", msg.Text);
    }

    [Fact]
    public void Le_format_lobby_est_desactive_dans_une_frame_in_game()
    {
        // Faux positif vu en test réel : le pseudo coréen « 큰 곰 » apparaît dans chaque
        // ping/achat ; un fragment d'OCR « 29 : 4 큰 곰 대… » passait par le format lobby.
        var messages = Assembler.Assemble(
        [
            "30:30 큰 곰 (Hwei) is on the way",      // marqueur in-game → frame en partie
            "29 : 4 큰 곰 대 yc",                    // fragment d'OCR → ne doit PAS parser
        ]);

        Assert.Empty(messages);
    }

    [Fact]
    public void Le_format_lobby_reste_actif_dans_une_frame_de_champ_select()
    {
        var messages = Assembler.Assemble(
        [
            "Krug : 진짜",
            "Krug : 연승치니까 주포절대안주네 4판연속",
        ]);

        Assert.Equal(2, messages.Count);
        Assert.All(messages, m => Assert.Equal("Lobby", m.Channel));
    }

    [Fact]
    public void Un_ping_au_timestamp_mutile_ne_contamine_pas_le_message_precedent()
    {
        // Vu en réel : « 17:48 나나오즈 (Syndra) is on the way » lu « 1748 난날Relsynahe n he way »
        // (timestamp sans ':', parenthèse perdue) se fusionnait dans le message du joueur.
        var messages = Assembler.Assemble(
        [
            "17:18 [Team] KIFFEUR IS BACK (Diana): play with kai'sa",
            "1748 난날Relsynahe n he way",
        ]);

        var msg = Assert.Single(messages);
        Assert.Equal("play with kai'sa", msg.Text);
    }

    [Theory]
    [InlineData("I1:50 신일(Rell has targeted the Blue Sentinel")] // ts « I1:50 »
    [InlineData("11.50 신 일 (Rell) is on the way")]               // ts « 11.50 »
    [InlineData("1748 난날 on he way")]                            // ts « 1748 »
    public void Les_timestamps_mutiles_marquent_une_nouvelle_entree(string line) =>
        Assert.True(ChatLineParser.LooksLikeNewEntry(line));

    [Fact]
    public void Mode_tout_afficher_copie_les_pings_anglais_et_traduit_les_coreens()
    {
        var messages = Assembler.Assemble(
        [
            "05:39 신 일 (Rell) is on the way",            // ping EN, pseudo hangul → copie
            "몬스터 (르블랑) 님이 후퇴 신호를 보냄",          // ping client CORÉEN → à traduire
            "B D",                                         // fragment court → ignoré
            "COIEUI",                                      // bruit de fondu → ignoré
            "2B Da Qs Compee",                             // idem (ni ts, ni champion, ni hangul)
            "-5Eeen Rey son th a a pae",                   // idem
        ], mirrorAllLines: true);

        Assert.Equal(2, messages.Count);
        Assert.All(messages, m => Assert.Equal("Sys", m.Channel));
        Assert.False(messages[0].NeedsTranslation); // copie telle quelle
        Assert.True(messages[1].NeedsTranslation);  // hangul majoritaire → traduction
    }

    [Fact]
    public void Mode_tout_afficher_ne_duplique_pas_le_chat_joueur()
    {
        var messages = Assembler.Assemble(
        [
            "13:51 [Team] 큰 곰 (Hwei): 원숭이마낭",
        ], mirrorAllLines: true);

        var msg = Assert.Single(messages);
        Assert.Equal("Team", msg.Channel); // parsé en chat joueur, pas en Sys
    }

    [Fact]
    public void Plusieurs_messages_simples_restent_separes()
    {
        var messages = Assembler.Assemble(
        [
            "24:26 [Team] 도구연구협회장 (Sylas): 아ㄴ;",
            "24:28 [Team] 도구연구협회장 (Sylas): 아니",
        ]);

        Assert.Equal(2, messages.Count);
    }
}
