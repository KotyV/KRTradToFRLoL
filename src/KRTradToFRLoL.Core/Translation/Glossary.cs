using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KRTradToFRLoL.Translation;

/// <summary>
/// Étage 0 : table de correspondance instantanée pour les messages ultra-fréquents.
/// Volontairement conservateur : ne traduit que si le message ENTIER (normalisé)
/// correspond à une entrée — les correspondances partielles sont laissées au LLM
/// (trop de faux positifs sinon : 갱 dans un mot composé, etc.).
/// </summary>
public sealed partial class Glossary
{
    private readonly Dictionary<string, string> _exact = new();
    private readonly Lock _lock = new();

    public Glossary() { }

    /// <summary>Construction directe pour les tests.</summary>
    public Glossary(IEnumerable<KeyValuePair<string, string>> entries)
    {
        foreach (var (kr, fr) in entries)
            _exact[Core.ChatMessage.Normalize(kr)] = fr;
    }

    public int Count
    {
        get { lock (_lock) return _exact.Count; }
    }

    /// <summary>Complète le glossaire (lexique Data Dragon…) SANS écraser les entrées vérifiées.</summary>
    public bool TryAdd(string kr, string fr)
    {
        if (string.IsNullOrWhiteSpace(kr) || string.IsNullOrWhiteSpace(fr)) return false;
        lock (_lock) return _exact.TryAdd(Core.ChatMessage.Normalize(kr), fr);
    }

    [GeneratedRegex("^[ㅋㅎ;~!.…ㅠㅜ ]+$")]
    private static partial Regex LaughOnlyRegex();

    public static Glossary LoadFromDataDir()
    {
        var g = new Glossary();
        var path = Path.Combine(AppContext.BaseDirectory, "data", "glossaire.json");
        try
        {
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var entry in doc.RootElement.GetProperty("entries").EnumerateArray())
                {
                    var kr = entry.GetProperty("kr").GetString();
                    var fr = entry.GetProperty("fr").GetString();
                    if (!string.IsNullOrWhiteSpace(kr) && !string.IsNullOrWhiteSpace(fr))
                        g._exact[Core.ChatMessage.Normalize(kr)] = fr;
                }
            }
        }
        catch { /* glossaire absent/corrompu → étage 0 inactif, le LLM prend tout */ }
        return g;
    }

    /// <summary>Traduction instantanée si le message entier est connu, sinon null.</summary>
    public string? TryTranslate(string koreanText)
    {
        var t = koreanText.Trim();
        if (t.Length == 0) return null;

        // Rires/soupirs purs : ㅋㅋㅋ, ㅎㅎ, ㅠㅠ...
        if (LaughOnlyRegex().IsMatch(t))
            return t.Contains('ㅠ') || t.Contains('ㅜ') ? "(pleure)" : "mdrr";

        lock (_lock) return _exact.GetValueOrDefault(Core.ChatMessage.Normalize(t));
    }
}
