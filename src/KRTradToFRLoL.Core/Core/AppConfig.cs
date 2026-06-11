using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using KRTradToFRLoL.Security;

namespace KRTradToFRLoL.Core;

/// <summary>
/// Configuration persistée dans %AppData%/KRTradToFRLoL/config.json.
/// Les secrets (clé API, token proxy) sont stockés chiffrés DPAPI — jamais en clair.
/// </summary>
public sealed class AppConfig
{
    // — Zone de chat (pixels physiques écran) —
    public int ChatRegionX { get; set; }
    public int ChatRegionY { get; set; } = 650;
    public int ChatRegionWidth { get; set; } = 560;
    public int ChatRegionHeight { get; set; } = 260;

    // — Overlay —
    public double OverlayX { get; set; } = 600;
    public double OverlayY { get; set; } = 600;
    public double OverlayWidth { get; set; } = 380;
    public double OverlayFontSize { get; set; } = 13;
    public int OverlayMessageLifetimeSeconds { get; set; } = 25;

    // — Traduction —
    public string ClaudeModel { get; set; } = "claude-haiku-4-5";
    public double TranslationTimeoutSeconds { get; set; } = 2.5;
    public double LocalTranslationTimeoutSeconds { get; set; } = 4.0;

    /// <summary>Clé API Anthropic, chiffrée DPAPI (mode direct, dev/perso).</summary>
    public string AnthropicApiKeyProtected { get; set; } = "";

    /// <summary>
    /// URL https du proxy de traduction (mode recommandé pour la distribution :
    /// la clé Anthropic reste côté serveur, l'app n'a qu'un token révocable).
    /// </summary>
    public string ProxyUrl { get; set; } = "";

    /// <summary>Token d'app délivré par l'opérateur du proxy, chiffré DPAPI.</summary>
    public string ProxyTokenProtected { get; set; } = "";

    /// <summary>Dossier des modèles de traduction locale (M2M-100 ONNX). Vide = dossier par défaut.</summary>
    public string LocalModelDirectory { get; set; } = "";

    /// <summary>Refléter TOUTES les lignes du chat (pings/système compris) dans l'overlay,
    /// pas seulement le chat joueur. Les lignes majoritairement coréennes (client KR) sont
    /// traduites, les autres copiées en gris. Décochable pour un overlay minimal en live.</summary>
    public bool MirrorAllLines { get; set; } = true;

    // — Capture/OCR —
    public int CaptureIntervalMs { get; set; } = 250;
    public double OcrUpscaleFactor { get; set; } = 2.0;

    /// <summary>Champ hérité des configs v0.1 (clé en clair) : migré puis vidé au chargement.</summary>
    [Obsolete("Migration uniquement — utiliser SetApiKey/ResolveApiKey.")]
    public string AnthropicApiKey { get; set; } = "";

    [JsonIgnore]
    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KRTradToFRLoL");

    /// <summary>
    /// Racine des modèles du bundle portable : dossier models/ à côté de l'exe.
    /// Ancré sur le chemin du process (pas AppContext.BaseDirectory) : en publication
    /// « exe unique », BaseDirectory pointe vers le cache d'extraction temporaire,
    /// alors que les modèles sont posés à côté du vrai .exe.
    /// </summary>
    private static readonly string PortableModelsRoot =
        Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "models");

    /// <summary>
    /// Un modèle livré à côté de l'exe (zip portable « dézipper → double-clic ») a priorité
    /// sur l'installation utilisateur de %AppData% (tools/export_*.py).
    /// </summary>
    internal static string ResolveModelDir(string portableRoot, string name)
    {
        var portable = Path.Combine(portableRoot, name);
        return Directory.Exists(portable) ? portable : Path.Combine(ConfigDir, "models", name);
    }

    [JsonIgnore]
    public string EffectiveOcrModelDirectory => ResolveModelDir(PortableModelsRoot, "ocr-ko");

    [JsonIgnore]
    public string EffectiveLocalModelDirectory =>
        string.IsNullOrWhiteSpace(LocalModelDirectory)
            ? ResolveModelDir(PortableModelsRoot, "m2m100")
            : LocalModelDirectory;

    private static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    public void SetApiKey(string plaintext) => AnthropicApiKeyProtected = SecretProtector.Protect(plaintext.Trim());

    public void SetProxyToken(string plaintext) => ProxyTokenProtected = SecretProtector.Protect(plaintext.Trim());

    /// <summary>Clé API : config chiffrée, sinon variable d'environnement ANTHROPIC_API_KEY.</summary>
    public string ResolveApiKey()
    {
        var fromConfig = SecretProtector.Unprotect(AnthropicApiKeyProtected);
        return fromConfig.Length > 0 ? fromConfig : Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
    }

    public string ResolveProxyToken() => SecretProtector.Unprotect(ProxyTokenProtected);

    public static AppConfig Load() => LoadFrom(ConfigPath);

    public void Save() => SaveTo(ConfigPath);

    /// <summary>Chargement testable depuis un chemin arbitraire (migration des secrets en clair incluse).</summary>
    public static AppConfig LoadFrom(string path)
    {
        AppConfig config = new();
        try
        {
            if (File.Exists(path))
                config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path)) ?? new AppConfig();
        }
        catch (JsonException) { /* config corrompue → valeurs par défaut */ }

#pragma warning disable CS0618 // migration du champ hérité v0.1
        if (!string.IsNullOrWhiteSpace(config.AnthropicApiKey))
        {
            config.SetApiKey(config.AnthropicApiKey);
            config.AnthropicApiKey = "";
            config.SaveTo(path); // réécrit immédiatement sans le secret en clair
        }
#pragma warning restore CS0618
        return config;
    }

    public void SaveTo(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
