using KRTradToFRLoL.Core;

namespace KRTradToFRLoL.Parsing;

/// <summary>
/// Le chat reste affiché plusieurs secondes : chaque OCR revoit les mêmes lignes.
/// Dédup en deux temps :
///  1. clé exacte normalisée (timestamp + champion + texte) — robuste si timestamps activés ;
///  2. distance d'édition contre les lignes récentes du même champion, pour absorber le
///     jitter d'OCR (fond animé → la même ligne peut varier d'1-2 caractères entre frames).
///     Seuil en distance ABSOLUE bornée (≤ max(1, 15 % de la longueur)) et non en ratio :
///     sur un message de 3 caractères, 1 caractère d'écart = 67 % de similarité seulement.
/// Les répétitions légitimes (spam « ㅋㅋㅋ ») restent acceptées dès que le timestamp change.
/// </summary>
public sealed class Deduplicator
{
    private const int WindowSize = 120;

    private readonly HashSet<string> _seenKeys = new();
    private readonly Queue<string> _keyOrder = new();
    private readonly Queue<(string Speaker, string Timestamp, string NormText)> _recent = new();

    public bool IsNew(ChatMessage msg)
    {
        var key = msg.DedupKey;
        if (_seenKeys.Contains(key)) return false;

        var norm = ChatMessage.Normalize(msg.Text);
        foreach (var (speaker, ts, text) in _recent)
        {
            // Jitter OCR : même auteur, même timestamp (ou timestamp illisible), texte quasi identique
            if (speaker.Equals(msg.SpeakerKey, StringComparison.OrdinalIgnoreCase)
                && (ts == msg.Timestamp || ts.Length == 0 || msg.Timestamp.Length == 0)
                && Levenshtein.Distance(text, norm) <= MaxOcrJitter(text, norm))
                return false;
        }

        _seenKeys.Add(key);
        _keyOrder.Enqueue(key);
        _recent.Enqueue((msg.SpeakerKey, msg.Timestamp, norm));
        while (_keyOrder.Count > WindowSize) _seenKeys.Remove(_keyOrder.Dequeue());
        while (_recent.Count > 40) _recent.Dequeue();
        return true;
    }

    private static int MaxOcrJitter(string a, string b) =>
        Math.Max(1, (int)(Math.Max(a.Length, b.Length) * 0.2));

    public void Reset()
    {
        _seenKeys.Clear();
        _keyOrder.Clear();
        _recent.Clear();
    }
}
