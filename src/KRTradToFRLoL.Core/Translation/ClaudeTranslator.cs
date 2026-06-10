using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KRTradToFRLoL.Core;

namespace KRTradToFRLoL.Translation;

/// <summary>
/// Traducteur LLM (Claude Haiku) en streaming SSE. Deux modes :
///  - proxy (recommandé en distribution) : l'app appelle un relais https opéré par le
///    mainteneur avec un token d'app révocable — la clé Anthropic ne quitte jamais le serveur ;
///  - direct (dev/perso) : clé API locale chiffrée DPAPI.
/// Le system prompt (contexte LoL + glossaire) dépasse 4096 tokens et est marqué
/// cache_control → mis en cache par Anthropic (lectures à 0,1× le prix).
/// </summary>
public sealed class ClaudeTranslator : ITranslator, IDisposable
{
    private const string AnthropicEndpoint = "https://api.anthropic.com/v1/messages";

    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly string _systemPrompt;

    public bool IsConfigured { get; }

    /// <summary>Mode effectif, pour affichage : "proxy", "direct" ou "non configuré".</summary>
    public string Mode { get; }

    /// <summary>Renseigné après chaque appel : vrai si le cache de prompt a fonctionné.</summary>
    public bool LastCallUsedCache { get; private set; }

    public ClaudeTranslator(AppConfig config)
    {
        _model = config.ClaudeModel;
        _systemPrompt = LoadSystemPrompt();
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var proxyUrl = config.ProxyUrl.Trim();
        if (proxyUrl.Length > 0)
        {
            // Mode proxy : https obligatoire (le token partirait en clair sinon).
            var token = config.ResolveProxyToken();
            var valid = Uri.TryCreate(proxyUrl, UriKind.Absolute, out var uri)
                        && uri.Scheme == Uri.UriSchemeHttps
                        && token.Length > 0;
            if (valid)
            {
                _endpoint = proxyUrl;
                _http.DefaultRequestHeaders.Add("x-app-token", token);
                IsConfigured = true;
                Mode = "proxy";
                return;
            }
            _endpoint = "";
            Mode = "non configuré";
            return;
        }

        var apiKey = config.ResolveApiKey();
        if (apiKey.Length > 0)
        {
            _endpoint = AnthropicEndpoint;
            _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
            IsConfigured = true;
            Mode = "direct";
            return;
        }

        _endpoint = "";
        Mode = "non configuré";
    }

    private static string LoadSystemPrompt()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data", "system-prompt.txt");
        if (File.Exists(path)) return File.ReadAllText(path);
        // Prompt minimal de secours si le fichier manque.
        return "Tu traduis du chat League of Legends du coréen vers le français, registre gamer français. " +
               "Le texte vient d'un OCR : tolère les typos et espaces erronés. " +
               "Réponds UNIQUEMENT avec la traduction, ultra-courte, sans guillemets ni explication. " +
               "Conserve pseudos, noms de champions et lettres de sorts (Q W E R). " +
               "ㅋㅋㅋ → mdrr. Ne pas adoucir les insultes. Texte déjà en anglais/français → tel quel.";
    }

    /// <summary>
    /// Sonde réelle de connectivité/authentification (mini-requête d'1 token de sortie) :
    /// distingue token refusé, URL fausse et serveur injoignable — l'enregistrement de la
    /// config seul ne contacte pas le serveur.
    /// </summary>
    public async Task<(bool Ok, string Detail)> ProbeAsync(CancellationToken ct)
    {
        if (!IsConfigured) return (false, "aucune source de traduction configurée");

        var body = new
        {
            model = _model,
            max_tokens = 1,
            stream = true,
            messages = new object[] { new { role = "user", content = "ping" } },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            return (int)response.StatusCode switch
            {
                200 => (true, Mode == "proxy" ? "proxy joignable, token accepté ✓" : "clé API acceptée ✓"),
                401 => (false, Mode == "proxy"
                    ? "le proxy répond mais REFUSE le token (vérifier APP_TOKENS côté Vercel, puis redéployer)"
                    : "clé API refusée"),
                404 => (false, "URL introuvable — vérifier le chemin (…/api/translate)"),
                405 => (false, "méthode refusée — l'URL pointe sur la racine du site, pas sur …/api/translate"),
                429 => (false, "quota/limite de débit atteint"),
                529 => (false, "API Anthropic surchargée (réessayer)"),
                var code => (false, $"réponse inattendue ({code})"),
            };
        }
        catch (HttpRequestException ex)
        {
            return (false, $"serveur injoignable : {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (false, "délai dépassé (réseau ?)");
        }
    }

    public async Task<string?> TranslateAsync(string koreanText, Action<string>? onPartial, CancellationToken ct)
    {
        if (!IsConfigured) return null;

        var body = new
        {
            model = _model,
            max_tokens = 200,
            stream = true,
            system = new object[]
            {
                new { type = "text", text = _systemPrompt, cache_control = new { type = "ephemeral" } },
            },
            messages = new object[]
            {
                new { role = "user", content = koreanText },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) return null;

        var sb = new StringBuilder();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            var payload = line[6..];
            if (payload == "[DONE]") break;

            using var doc = JsonDocument.Parse(payload);
            var type = doc.RootElement.GetProperty("type").GetString();
            switch (type)
            {
                case "message_start":
                    LastCallUsedCache =
                        doc.RootElement.TryGetProperty("message", out var msg)
                        && msg.TryGetProperty("usage", out var usage)
                        && usage.TryGetProperty("cache_read_input_tokens", out var cached)
                        && cached.GetInt64() > 0;
                    break;

                case "content_block_delta":
                    if (doc.RootElement.GetProperty("delta").TryGetProperty("text", out var delta))
                    {
                        sb.Append(delta.GetString());
                        onPartial?.Invoke(sb.ToString());
                    }
                    break;

                case "message_stop":
                    goto done;
            }
        }
    done:
        var result = sb.ToString().Trim();
        return result.Length > 0 ? result : null;
    }

    public void Dispose() => _http.Dispose();
}
