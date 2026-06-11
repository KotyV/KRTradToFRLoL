using KRTradToFRLoL.Core;
using KRTradToFRLoL.Parsing;
using Xunit;

namespace KRTradToFRLoL.Tests;

public class DeduplicatorTests
{
    private static ChatMessage Msg(string ts, string champ, string text) => new()
    {
        Timestamp = ts,
        Channel = "Team",
        Author = "joueur",
        Champion = champ,
        Text = text,
        RawLine = $"{ts} [Team] joueur ({champ}): {text}",
    };

    [Fact]
    public void Premiere_vue_acceptee_doublon_exact_rejete()
    {
        var dedup = new Deduplicator();
        var msg = Msg("16:47", "Lucian", "에반데");

        Assert.True(dedup.IsNew(msg));
        Assert.False(dedup.IsNew(msg));
    }

    [Fact]
    public void Jitter_ocr_un_caractere_rejete_meme_sur_message_court()
    {
        var dedup = new Deduplicator();
        Assert.True(dedup.IsNew(Msg("16:47", "Lucian", "에반데")));
        Assert.False(dedup.IsNew(Msg("16:47", "Lucian", "예반데")));
    }

    [Fact]
    public void Meme_texte_timestamp_different_accepte()
    {
        var dedup = new Deduplicator();
        Assert.True(dedup.IsNew(Msg("16:47", "Lucian", "에반데")));
        Assert.True(dedup.IsNew(Msg("18:02", "Lucian", "에반데")));
    }

    [Fact]
    public void Deux_messages_differents_du_meme_auteur_a_la_meme_seconde_acceptes()
    {
        // Capture 16 : « 13:52 흰 곰 (Hwei): 쳐대주고 » puis « 13:52 흰 곰 (Hwei): 입은 »
        var dedup = new Deduplicator();
        Assert.True(dedup.IsNew(Msg("13:52", "Hwei", "쳐대주고")));
        Assert.True(dedup.IsNew(Msg("13:52", "Hwei", "입은")));
    }

    [Fact]
    public void Meme_texte_champion_different_accepte()
    {
        var dedup = new Deduplicator();
        Assert.True(dedup.IsNew(Msg("16:47", "Lucian", "ㄱㄱ")));
        Assert.True(dedup.IsNew(Msg("16:47", "Yuumi", "ㄱㄱ")));
    }

    [Fact]
    public void Reset_oublie_tout()
    {
        var dedup = new Deduplicator();
        var msg = Msg("16:47", "Lucian", "에반데");
        Assert.True(dedup.IsNew(msg));
        dedup.Reset();
        Assert.True(dedup.IsNew(msg));
    }
}
