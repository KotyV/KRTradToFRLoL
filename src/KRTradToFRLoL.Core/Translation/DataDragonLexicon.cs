using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace KRTradToFRLoL.Translation;

/// <summary>
/// Lexique officiel coréen → français tiré de Data Dragon (API statique Riot) :
/// noms de champions, d'objets et de sorts d'invocateur, toujours à jour du patch.
/// Fusionné dans le glossaire (correspondance exacte) et la pré-normalisation du
/// traducteur local — les entrées vérifiées à la main gardent la priorité
/// (les joueurs FR disent « flash », pas « Saut éclair »).
/// </summary>
public sealed class DataDragonLexicon
{
    private static readonly string[] DataFiles = ["champion.json", "item.json", "summoner.json"];
    private readonly Dictionary<string, string> _koToFr = new(StringComparer.Ordinal);

    public int Count => _koToFr.Count;

    public IReadOnlyDictionary<string, string> KoToFr => _koToFr;

    private static string CachePath => Path.Combine(Core.AppConfig.ConfigDir, "ddragon-lexicon.json");

    public async Task LoadAsync()
    {
        if (TryLoadFromCache()) return;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var versions = JsonSerializer.Deserialize<string[]>(
                await http.GetStringAsync("https://ddragon.leagueoflegends.com/api/versions.json"));
            var latest = versions?.FirstOrDefault() ?? "15.1.1";

            foreach (var file in DataFiles)
            {
                using var ko = JsonDocument.Parse(await http.GetStringAsync(
                    $"https://ddragon.leagueoflegends.com/cdn/{latest}/data/ko_KR/{file}"));
                using var fr = JsonDocument.Parse(await http.GetStringAsync(
                    $"https://ddragon.leagueoflegends.com/cdn/{latest}/data/fr_FR/{file}"));
                foreach (var (koName, frName) in ExtractPairs(ko, fr))
                    _koToFr.TryAdd(koName, frName);
            }

            Directory.CreateDirectory(Core.AppConfig.ConfigDir);
            await File.WriteAllTextAsync(CachePath, JsonSerializer.Serialize(_koToFr));
        }
        catch (HttpRequestException) { /* hors ligne sans cache → lexique vide, l'app vit sans */ }
        catch (TaskCanceledException) { /* timeout réseau → idem */ }
        catch (JsonException) { /* réponse inattendue → idem */ }
    }

    /// <summary>Apparie les noms ko/fr par clé d'entrée (« data » de Data Dragon). Testable hors ligne.</summary>
    public static IEnumerable<KeyValuePair<string, string>> ExtractPairs(JsonDocument korean, JsonDocument french)
    {
        if (!korean.RootElement.TryGetProperty("data", out var koData)
            || !french.RootElement.TryGetProperty("data", out var frData))
            yield break;

        foreach (var entry in koData.EnumerateObject())
        {
            if (!frData.TryGetProperty(entry.Name, out var frEntry)) continue;
            var koName = entry.Value.TryGetProperty("name", out var k) ? k.GetString() : null;
            var frName = frEntry.TryGetProperty("name", out var f) ? f.GetString() : null;
            if (string.IsNullOrWhiteSpace(koName) || string.IsNullOrWhiteSpace(frName)) continue;
            if (koName == frName) continue; // identique (rare) → rien à traduire

            yield return new KeyValuePair<string, string>(koName.Trim(), frName.Trim());
        }
    }

    private bool TryLoadFromCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return false;
            var cached = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CachePath));
            if (cached is null || cached.Count == 0) return false;
            foreach (var (ko, fr) in cached) _koToFr.TryAdd(ko, fr);
            return true;
        }
        catch (JsonException) { return false; }
        catch (IOException) { return false; }
    }
}
