using System.Text.RegularExpressions;
using KRTradToFRLoL.Core;

namespace KRTradToFRLoL.Parsing;

/// <summary>
/// Filtre STRUCTUREL des lignes OCR (cf. docs/observations-captures.md) :
/// seules les lignes « [Team|Équipe|All|Tous] Pseudo (Champion): message » sont du chat
/// de joueur — et on ne garde que celles dont le message contient du hangul.
/// Tout le reste (pings « is on the way », kills, achats, système) est ignoré,
/// même s'il contient des pseudos coréens.
/// </summary>
public sealed partial class ChatLineParser(ChampionNames champions)
{
    // 12:34 optionnel + [Canal] (le canal peut être mal OCRisé : crochets tolérés absents)
    [GeneratedRegex(@"^\s*(?<ts>\d{1,2}\s*:\s*\d{2})?\s*[\[\(]?(?<chan>Team|Équipe|Equipe|All|Tous|Party|Groupe|팀|아군|전체|모두|파티)[\]\)]?\s*(?<rest>.+)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex ChannelLineRegex();

    // Dans le reste : « Pseudo (Champion) : message » — on ancre sur la DERNIÈRE
    // parenthèse suivie de ':' car le pseudo peut contenir espaces et parenthèses ratées.
    [GeneratedRegex(@"^(?<author>.*?)\((?<champ>[^()]{2,30})\)\s*[:;]\s*(?<msg>.+)$")]
    private static partial Regex AuthorChampMsgRegex();

    // Chat du champ select (fenêtre client) : « Pseudo : message » — pas de tag de canal,
    // pas de parenthèse champion, pas de timestamp. En ranked les coéquipiers sont
    // anonymisés en camps de jungle (« Krug », « Murk Wolf »…).
    [GeneratedRegex(@"^(?<author>[^():\[\]]{1,24}?)\s*:\s*(?<msg>.+)$")]
    private static partial Regex ChampSelectLineRegex();

    [GeneratedRegex(@"[가-힣ᄀ-ᇿ㄰-㆏]")]
    private static partial Regex HangulRegex();

    // Marqueurs des messages système du client coréen (pings « 님이 … 보냄 », achats,
    // annonces) : une ligne orpheline qui en contient n'est PAS une continuation de chat.
    [GeneratedRegex(@"님이|님에게|신호를|보냄|알림|구입했|파괴했|처치했|습니다")]
    private static partial Regex KoreanSystemFragmentRegex();

    // Une ligne qui commence par un timestamp ou contient une parenthèse « (Champion) »
    // est une NOUVELLE entrée du chat (ping/kill/achat avec pseudo coréen compris),
    // jamais la suite repliée d'un message précédent.
    [GeneratedRegex(@"^\s*\d{1,2}\s*:\s*\d{2}|\([^()]{2,30}\)")]
    private static partial Regex NewEntryMarkerRegex();

    public static bool ContainsHangul(string s) => HangulRegex().IsMatch(s);

    public static bool LooksLikeKoreanSystemFragment(string s) => KoreanSystemFragmentRegex().IsMatch(s);

    public static bool LooksLikeNewEntry(string s) => NewEntryMarkerRegex().IsMatch(s);

    /// <summary>
    /// Parse une ligne OCR ; null si ce n'est pas un message de chat coréen.
    /// <paramref name="allowChampSelect"/> : le format lobby (« Krug : … ») ne doit être
    /// tenté QUE hors partie — en jeu, les fragments d'OCR contenant le pseudo coréen d'un
    /// joueur s'y engouffrent (faux positifs vus en test réel).
    /// </summary>
    public ChatMessage? Parse(string rawLine, bool allowChampSelect = true)
    {
        var m = ChannelLineRegex().Match(rawLine);
        if (!m.Success) return allowChampSelect ? ParseChampSelect(rawLine) : null;

        var rest = m.Groups["rest"].Value.Trim();

        // Ancre sur la dernière occurrence "(champ):" → regex greedy sur author fait l'affaire
        var acm = AuthorChampMsgRegex().Match(rest);
        if (!acm.Success) return null;

        var champion = acm.Groups["champ"].Value.Trim();
        var message = acm.Groups["msg"].Value.Trim();

        // Le message doit contenir du coréen, sinon rien à traduire
        // (les pings/système avec ':' sont en langue du client : exclus ici).
        if (!ContainsHangul(message)) return null;

        // Validation champion (tolérante aux erreurs d'OCR ; ne bloque pas si liste absente)
        if (!champions.IsChampion(champion)) return null;

        return new ChatMessage
        {
            Timestamp = m.Groups["ts"].Value.Replace(" ", ""),
            Channel = NormalizeChannel(m.Groups["chan"].Value),
            Author = acm.Groups["author"].Value.Trim(),
            Champion = champion,
            Text = message,
            RawLine = rawLine,
        };
    }

    // Auteur champ select : lettres (hangul compris), chiffres, espace et ponctuation de
    // pseudo — le bruit d'OCR (« 1003 Tbe ,] », « 「;•nn ») contient toujours autre chose.
    [GeneratedRegex(@"^[\p{L}\p{N}][\p{L}\p{N} ._'-]{0,22}$")]
    private static partial Regex CleanAuthorRegex();

    /// <summary>
    /// Format champ select : « Krug : 진짜 ». Volontairement strict — ce format sans ancre
    /// champion est la cible favorite du bruit d'OCR (vu en test réel : une annonce système
    /// anglaise massacrée + un hangul halluciné suffisaient à passer) :
    /// auteur au charset propre, message MAJORITAIREMENT coréen, pas de timestamp ni de
    /// parenthèse (sinon c'est une entrée in-game), pas de fragment système coréen.
    /// </summary>
    private static ChatMessage? ParseChampSelect(string rawLine)
    {
        if (LooksLikeNewEntry(rawLine)) return null; // timestamp ou (Champion) → format in-game
        var m = ChampSelectLineRegex().Match(rawLine);
        if (!m.Success) return null;

        var author = m.Groups["author"].Value.Trim();
        var message = m.Groups["msg"].Value.Trim();
        if (!CleanAuthorRegex().IsMatch(author)) return null;
        if (author.All(char.IsDigit)) return null; // « 29 », « 3052 »… = fragment d'OCR, pas un pseudo
        if (!IsMostlyHangul(message) || LooksLikeKoreanSystemFragment(rawLine)) return null;

        return new ChatMessage
        {
            Timestamp = "",
            Channel = "Lobby",
            Author = author,
            Champion = "",
            Text = message,
            RawLine = rawLine,
        };
    }

    /// <summary>Au moins 2 caractères hangul ET au moins la moitié des lettres en hangul :
    /// un glyphe coréen halluciné par l'OCR dans une ligne anglaise ne suffit pas.</summary>
    public static bool IsMostlyHangul(string s)
    {
        int hangul = 0, letters = 0;
        foreach (var c in s)
        {
            if (c is >= '가' and <= '힣' or >= 'ㄱ' and <= 'ㆎ') hangul++;
            if (char.IsLetter(c)) letters++;
        }
        return hangul >= 2 && hangul * 2 >= letters;
    }

    private static string NormalizeChannel(string chan) => chan.ToLowerInvariant() switch
    {
        "équipe" or "equipe" or "team" or "팀" or "아군" => "Team",
        "all" or "tous" or "전체" or "모두" => "All",
        "party" or "groupe" or "파티" => "Party",
        _ => chan,
    };
}
