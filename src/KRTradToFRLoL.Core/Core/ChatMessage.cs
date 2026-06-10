namespace KRTradToFRLoL.Core;

/// <summary>Une ligne de chat parsée depuis l'OCR.</summary>
public sealed record ChatMessage
{
    public string Timestamp { get; init; } = "";      // "16:47" si timestamps activés, sinon ""
    public string Channel { get; init; } = "";        // Team / All / Party (normalisé)
    public string Author { get; init; } = "";         // pseudo brut (peut être du hangul)
    public string Champion { get; init; } = "";       // nom de champion (ancre de parsing)
    public string Text { get; init; } = "";           // le message seul, à traduire
    public string RawLine { get; init; } = "";        // ligne OCR complète (diagnostic)

    /// <summary>Faux pour un message de chat sans hangul (anglais…) : affiché tel quel
    /// dans l'overlay, sans passer par la traduction — le flux reste complet.</summary>
    public bool NeedsTranslation { get; init; } = true;

    /// <summary>Identité de l'émetteur : champion in-game, pseudo (anonymisé ou non) en champ select.</summary>
    public string SpeakerKey => Champion.Length > 0 ? Champion : Author;

    /// <summary>Clé de déduplication normalisée.</summary>
    public string DedupKey => $"{Timestamp}|{SpeakerKey}|{Normalize(Text)}";

    public static string Normalize(string s)
    {
        var collapsed = System.Text.RegularExpressions.Regex.Replace(s, "[ㅋㅎ]{2,}", "ㅋㅋ");
        return new string(collapsed.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
    }
}
