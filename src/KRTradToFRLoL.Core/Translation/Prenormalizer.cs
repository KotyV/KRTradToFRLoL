using System.IO;
using System.Text.Json;

namespace KRTradToFRLoL.Translation;

/// <summary>
/// Pré-normalisation pour la traduction locale (NMT générique) : remplace les tokens
/// d'argot LoL par leur équivalent en coréen standard que le modèle comprend
/// (갱 → 기습, 서렌 → 항복…). Remplacement par token ENTIER délimité par espaces
/// uniquement — le coréen est agglutinant, toute substitution partielle est dangereuse.
/// Inutile pour le LLM (qui connaît l'argot via le system prompt).
/// </summary>
public sealed class Prenormalizer
{
    private readonly Dictionary<string, string> _map;
    private readonly Lock _lock = new();

    public Prenormalizer(IReadOnlyDictionary<string, string>? pairs = null)
    {
        _map = pairs is null ? new Dictionary<string, string>() : new Dictionary<string, string>(pairs);
    }

    public int Count
    {
        get { lock (_lock) return _map.Count; }
    }

    /// <summary>Complète la table (lexique Data Dragon…) sans écraser les paires vérifiées.
    /// Tokens entiers uniquement : les noms multi-mots sont refusés.</summary>
    public bool TryAdd(string from, string to)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) || from.Contains(' ')) return false;
        lock (_lock) return _map.TryAdd(from, to);
    }

    public static Prenormalizer LoadFromDataDir()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "prenorm.json");
        var map = new Dictionary<string, string>();
        try
        {
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var pair in doc.RootElement.GetProperty("pairs").EnumerateArray())
                {
                    var from = pair.GetProperty("from").GetString();
                    var to = pair.GetProperty("to").GetString();
                    if (!string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to))
                        map[from] = to;
                }
            }
        }
        catch (JsonException) { /* table absente/corrompue → pré-normalisation inactive */ }
        return new Prenormalizer(map);
    }

    public string Apply(string text)
    {
        if (text.Length == 0) return text;
        var tokens = text.Split(' ', StringSplitOptions.None);
        var changed = false;
        lock (_lock)
        {
            if (_map.Count == 0) return text;
            for (var i = 0; i < tokens.Length; i++)
            {
                if (_map.TryGetValue(tokens[i], out var replacement))
                {
                    tokens[i] = replacement;
                    changed = true;
                }
            }
        }
        return changed ? string.Join(' ', tokens) : text;
    }
}
