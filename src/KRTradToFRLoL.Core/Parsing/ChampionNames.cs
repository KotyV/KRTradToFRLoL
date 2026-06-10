using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace KRTradToFRLoL.Parsing;

/// <summary>
/// Liste des noms de champions par locale via Data Dragon (API statique Riot, autorisée).
/// Les clients des streamers peuvent être en français (« Maître Yi ») ou en anglais —
/// on charge les deux. Cache disque pour fonctionner hors ligne ensuite.
/// </summary>
public sealed class ChampionNames
{
    // fr_FR (« Maître Yi »), en_US, et ko_KR : des streamers jouent avec le client
    // 100 % coréen sur le serveur KR (champions « 요네 », « 사일러스 »…).
    private static readonly string[] Locales = ["fr_FR", "en_US", "ko_KR"];
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    public ChampionNames() { }

    /// <summary>Construction directe avec une liste de noms (tests, mode hors ligne).</summary>
    public ChampionNames(IEnumerable<string> names)
    {
        foreach (var n in names) _names.Add(Normalize(n));
        _names.RemoveWhere(string.IsNullOrWhiteSpace);
    }

    public bool IsLoaded => _names.Count > 0;

    /// <summary>Vrai si le token ressemble à un nom de champion connu (tolère 1-2 erreurs d'OCR via préfixe).</summary>
    public bool IsChampion(string token)
    {
        if (_names.Count == 0) return true; // pas de liste → on ne bloque pas le parsing
        var t = Normalize(token);
        if (_names.Contains(t)) return true;
        // Tolérance OCR : match si un nom connu est à distance <= 1 sur les noms courts,
        // ou partage un préfixe de 4+ caractères (les confusions OCR touchent surtout la fin).
        return _names.Any(n =>
            (n.Length >= 4 && t.Length >= 4 && n.AsSpan(0, 4).SequenceEqual(t.AsSpan(0, Math.Min(4, t.Length)))) ||
            Levenshtein.Distance(n, t) <= 1);
    }

    private static string Normalize(string s) =>
        new(s.Where(char.IsLetterOrDigit).ToArray());

    private static string CachePath => Path.Combine(Core.AppConfig.ConfigDir, "champions.json");

    public async Task LoadAsync()
    {
        if (TryLoadFromCache()) return;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var versions = JsonSerializer.Deserialize<string[]>(
                await http.GetStringAsync("https://ddragon.leagueoflegends.com/api/versions.json"));
            var latest = versions?.FirstOrDefault() ?? "15.1.1";

            foreach (var locale in Locales)
            {
                var json = await http.GetStringAsync(
                    $"https://ddragon.leagueoflegends.com/cdn/{latest}/data/{locale}/champion.json");
                using var doc = JsonDocument.Parse(json);
                foreach (var champ in doc.RootElement.GetProperty("data").EnumerateObject())
                    _names.Add(Normalize(champ.Value.GetProperty("name").GetString() ?? ""));
            }
            _names.RemoveWhere(string.IsNullOrWhiteSpace);

            Directory.CreateDirectory(Core.AppConfig.ConfigDir);
            await File.WriteAllTextAsync(CachePath, JsonSerializer.Serialize(_names.ToArray()));
        }
        catch
        {
            // Hors ligne et pas de cache : le parseur acceptera n'importe quel token champion.
        }
    }

    private bool TryLoadFromCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return false;
            var cached = JsonSerializer.Deserialize<string[]>(File.ReadAllText(CachePath));
            if (cached is null || cached.Length == 0) return false;
            foreach (var n in cached) _names.Add(n);
            return true;
        }
        catch { return false; }
    }
}

public static class Levenshtein
{
    public static int Distance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    /// <summary>Similarité normalisée 0..1.</summary>
    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0) return 1;
        var max = Math.Max(a.Length, b.Length);
        return 1.0 - (double)Distance(a, b) / max;
    }
}
