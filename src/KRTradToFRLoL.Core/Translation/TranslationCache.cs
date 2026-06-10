using System.IO;
using System.Text.Json;

namespace KRTradToFRLoL.Translation;

/// <summary>
/// Étage 1 : cache persistant des traductions (un même message n'est traduit qu'une fois).
/// </summary>
public sealed class TranslationCache
{
    private const int MaxEntries = 5000;
    private readonly Dictionary<string, string> _map = new();
    private readonly LinkedList<string> _lru = new();
    private readonly Lock _lock = new();
    private readonly string? _path;

    /// <summary>Cache en mémoire seule (tests) ou adossé à un fichier.</summary>
    public TranslationCache(string? path = null) => _path = path;

    private static string DefaultPath => Path.Combine(Core.AppConfig.ConfigDir, "translation-cache.json");

    public static TranslationCache Load() => LoadFrom(DefaultPath);

    public static TranslationCache LoadFrom(string path)
    {
        var c = new TranslationCache(path);
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                if (loaded is not null)
                {
                    foreach (var (k, v) in loaded)
                    {
                        c._map[k] = v;
                        c._lru.AddLast(k);
                    }
                }
            }
        }
        catch (JsonException) { /* cache corrompu → on repart à vide */ }
        catch (IOException) { /* idem */ }
        return c;
    }

    public string? Get(string koreanText)
    {
        lock (_lock)
        {
            return _map.GetValueOrDefault(Core.ChatMessage.Normalize(koreanText));
        }
    }

    public void Put(string koreanText, string translation)
    {
        lock (_lock)
        {
            var key = Core.ChatMessage.Normalize(koreanText);
            if (_map.TryAdd(key, translation))
            {
                _lru.AddLast(key);
                if (_lru.Count > MaxEntries)
                {
                    var oldest = _lru.First!.Value;
                    _lru.RemoveFirst();
                    _map.Remove(oldest);
                }
            }
        }
    }

    public void Save()
    {
        if (_path is null) return;
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(_map));
            }
        }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }
}
