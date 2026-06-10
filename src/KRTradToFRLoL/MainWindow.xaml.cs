using System.ComponentModel;
using System.IO;
using System.Windows;
using KRTradToFRLoL.Core;
using KRTradToFRLoL.Ocr;
using KRTradToFRLoL.Overlay;
using KRTradToFRLoL.Parsing;
using KRTradToFRLoL.Translation;
using KRTradToFRLoL.Translation.Local;

namespace KRTradToFRLoL;

public partial class MainWindow : Window
{
    private readonly AppConfig _config = AppConfig.Load();
    private readonly ChampionNames _champions = new();
    private readonly IOcrEngine _ocr = new WindowsMediaOcrEngine();
    private readonly TranslationCache _cache = TranslationCache.Load();
    private readonly Glossary _glossary = Glossary.LoadFromDataDir();
    private readonly Prenormalizer _prenorm = Prenormalizer.LoadFromDataDir();

    private ClaudeTranslator? _llm;
    private M2M100Translator? _localNmt;
    private TranslatorService? _service;
    private OverlayWindow? _overlay;
    private bool _editMode;
    private List<string> _prevOcrLines = [];

    public MainWindow()
    {
        InitializeComponent();
        ProxyUrlBox.Text = _config.ProxyUrl;
        UpdateRegionLabel();
        Loaded += async (_, _) =>
        {
            if (!_ocr.IsAvailable)
                SetStatus($"⚠ OCR indisponible — {_ocr.UnavailableReason}", error: true);
            else
                SetStatus($"OCR prêt : {_ocr.Name}. Glossaire : {_glossary.Count} entrées. {DescribeTranslationSetup()}");

            await _champions.LoadAsync();
            AppendDiag(_champions.IsLoaded
                ? "Noms de champions chargés (fr/en/ko) via Data Dragon."
                : "⚠ Data Dragon inaccessible : validation des champions désactivée (parsing plus permissif).");

            _localNmt = M2M100Translator.TryCreate(_config.EffectiveLocalModelDirectory, _prenorm);
            AppendDiag(_localNmt is not null
                ? "Traduction locale M2M-100 chargée (filet hors ligne actif)."
                : $"Traduction locale absente (optionnel) — modèles attendus dans {_config.EffectiveLocalModelDirectory} (cf. tools/export_m2m100.py).");

            // Lexique officiel ko→fr (champions, objets, sorts) : complète le glossaire et la
            // pré-normalisation sans jamais écraser les entrées vérifiées à la main.
            var lexicon = new DataDragonLexicon();
            await lexicon.LoadAsync();
            if (lexicon.Count > 0)
            {
                int toGlossary = 0, toPrenorm = 0;
                foreach (var (ko, fr) in lexicon.KoToFr)
                {
                    if (_glossary.TryAdd(ko, fr)) toGlossary++;
                    if (_prenorm.TryAdd(ko, fr)) toPrenorm++;
                }
                AppendDiag($"Lexique Data Dragon : {lexicon.Count} noms officiels ko→fr (+{toGlossary} glossaire, +{toPrenorm} prénormalisation).");
            }
            else
            {
                AppendDiag("⚠ Lexique Data Dragon indisponible (hors ligne ?) — noms d'objets coréens non couverts.");
            }
        };
    }

    private string DescribeTranslationSetup()
    {
        using var probe = new ClaudeTranslator(_config);
        return probe.Mode switch
        {
            "proxy" => "Traduction : via proxy (clé côté serveur).",
            "direct" => "Traduction : API directe (clé locale chiffrée).",
            _ => "⚠ Pas de clé ni de proxy : glossaire + traduction locale uniquement.",
        };
    }

    private async void OnSaveKey(object sender, RoutedEventArgs e)
    {
        _config.SetApiKey(ApiKeyBox.Password);
        _config.Save();
        ApiKeyBox.Clear(); // la clé ne reste ni affichée ni en mémoire UI
        ResetServiceForConfigChange();
        await ProbeAndReportAsync("Clé API enregistrée (chiffrée DPAPI).");
    }

    /// <summary>Enregistrer ne suffit pas : on sonde réellement le serveur et on affiche le verdict.</summary>
    private async Task ProbeAndReportAsync(string prefix)
    {
        SetStatus($"{prefix} Test de connexion…");
        using var probe = new ClaudeTranslator(_config);
        if (!probe.IsConfigured)
        {
            SetStatus($"{prefix} {DescribeTranslationSetup()}", error: true);
            return;
        }
        var (ok, detail) = await probe.ProbeAsync(CancellationToken.None);
        SetStatus($"{prefix} {detail}", error: !ok);
        AppendDiag($"[sonde {probe.Mode}] {detail}");
    }

    private async void OnSaveProxy(object sender, RoutedEventArgs e)
    {
        _config.ProxyUrl = ProxyUrlBox.Text.Trim();
        if (ProxyTokenBox.Password.Length > 0)
            _config.SetProxyToken(ProxyTokenBox.Password);
        if (_config.ProxyUrl.Length == 0)
            _config.ProxyTokenProtected = ""; // proxy désactivé → on ne garde pas de token orphelin
        _config.Save();
        ProxyTokenBox.Clear();
        ResetServiceForConfigChange();
        await ProbeAndReportAsync("Proxy enregistré.");
    }

    private void ResetServiceForConfigChange()
    {
        if (_service?.IsRunning == true) _service.Stop();
        _service?.Dispose();
        _service = null;
        _llm?.Dispose();
        _llm = null;
        StartStopButton.Content = "2. Démarrer la traduction";
    }

    private void OnSelectRegion(object sender, RoutedEventArgs e)
    {
        var selector = new RegionSelectorWindow();
        if (selector.ShowDialog() == true && selector.SelectedRegion is { } region)
        {
            _config.ChatRegionX = region.X;
            _config.ChatRegionY = region.Y;
            _config.ChatRegionWidth = region.Width;
            _config.ChatRegionHeight = region.Height;
            _config.Save();
            UpdateRegionLabel();
            SetStatus("Zone de chat enregistrée.");
        }
    }

    private void OnStartStop(object sender, RoutedEventArgs e)
    {
        if (_service?.IsRunning == true)
        {
            _service.Stop();
            _cache.Save();
            StartStopButton.Content = "2. Démarrer la traduction";
            return;
        }

        if (!_ocr.IsAvailable)
        {
            SetStatus($"⚠ Impossible de démarrer — {_ocr.UnavailableReason}", error: true);
            return;
        }

        _overlay ??= new OverlayWindow(_config);
        if (_service is null)
        {
            _service = new TranslatorService(_config, _ocr, _champions, BuildPipeline());
            _service.StatusChanged += msg => Dispatcher.BeginInvoke(() => SetStatus(msg));
            _service.OcrFrame += lines => Dispatcher.BeginInvoke(() =>
            {
                // Dédup FLOUE contre la frame précédente : l'OCR fait varier 1-3 caractères
                // par ligne à chaque frame (fond animé) — l'égalité stricte ne suffit pas
                // et le diagnostic affichait les mêmes lignes en boucle.
                foreach (var l in lines.Where(IsNewOcrLine).Take(8))
                    AppendDiag($"[ocr] {l}");
                _prevOcrLines = [.. lines];
            });
            _service.MessageUpdated += evt => Dispatcher.BeginInvoke(() => OnMessage(evt));
        }

        _service.Start();
        StartStopButton.Content = "■ Arrêter";
    }

    private TranslationPipeline BuildPipeline()
    {
        _llm ??= new ClaudeTranslator(_config);
        return new TranslationPipeline(
            _glossary, _cache,
            _llm.IsConfigured ? _llm : null,
            _localNmt,
            _config.TranslationTimeoutSeconds,
            _config.LocalTranslationTimeoutSeconds);
    }

    private int _testCounter;

    /// <summary>Valide toute la chaîne de traduction sans partie en cours.</summary>
    private async void OnTestTranslate(object sender, RoutedEventArgs e)
    {
        var text = TestInputBox.Text.Trim();
        if (text.Length == 0) return;

        _overlay ??= new OverlayWindow(_config);
        var key = $"test-{++_testCounter}";
        _overlay.Upsert(key, "[Test] ", text, failed: true);
        AppendDiag($"[test] {text} → …");

        var pipeline = BuildPipeline();
        var result = await pipeline.TranslateAsync(
            text,
            partial => Dispatcher.BeginInvoke(() => _overlay?.Upsert(key, "[Test] ", partial, false)),
            CancellationToken.None);

        _overlay?.Upsert(key, "[Test] ", result.Text, result.Failed);
        AppendDiag($"[test → {result.Source}] {text} → {result.Text}");
        if (result.Failed)
            SetStatus("Test : aucune source de traduction n'a répondu (ni proxy/clé, ni modèle local). " + DescribeTranslationSetup(), error: true);
    }

    private void OnMessage(PipelineEvent evt)
    {
        // Le timestamp du jeu sert d'ancre visuelle : la ligne « 24:28 » de l'overlay
        // correspond à la ligne « 24:28 » du chat, même après défilement.
        // En champ select il n'y a ni timestamp ni champion → pseudo (anonymisé) seul.
        var header = evt.Message.Timestamp.Length > 0
            ? $"{evt.Message.Timestamp} [{evt.Message.SpeakerKey}] "
            : $"[{evt.Message.SpeakerKey}] ";
        var key = evt.Message.DedupKey;

        if (evt.Translation is { } result)
        {
            _overlay?.Upsert(key, header, result.Text, result.Failed);
            AppendDiag($"[{result.Source}] {evt.Message.Champion}: {evt.Message.Text} → {result.Text}");
        }
        else if (evt.PartialText is { } partial)
        {
            _overlay?.Upsert(key, header, partial, failed: false);
        }
        else
        {
            _overlay?.Upsert(key, header, evt.Message.Text, failed: true); // coréen brut en attendant
        }
    }

    private void OnToggleEditOverlay(object sender, RoutedEventArgs e)
    {
        _overlay ??= new OverlayWindow(_config);
        _editMode = !_editMode;
        if (_editMode && !_overlay.IsVisible) _overlay.Show();
        _overlay.SetEditMode(_editMode);
        EditOverlayButton.Content = _editMode ? "✔ Terminer le placement" : "Déplacer l'overlay";
    }

    private bool IsNewOcrLine(string line) =>
        line.Length >= 3 && !_prevOcrLines.Any(prev =>
            Parsing.Levenshtein.Distance(prev, line) <= Math.Max(2, (int)(Math.Max(prev.Length, line.Length) * 0.2)));

    private void UpdateRegionLabel() =>
        RegionLabel.Text = $"zone : {_config.ChatRegionX},{_config.ChatRegionY} {_config.ChatRegionWidth}×{_config.ChatRegionHeight}px";

    private void SetStatus(string msg, bool error = false)
    {
        StatusLabel.Text = msg;
        StatusLabel.Foreground = error
            ? System.Windows.Media.Brushes.Orange
            : (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FF8BE28B")!;
    }

    private void AppendDiag(string line)
    {
        DiagnosticList.Items.Add($"{DateTime.Now:HH:mm:ss} {line}");
        while (DiagnosticList.Items.Count > 400) DiagnosticList.Items.RemoveAt(0);
        DiagnosticList.ScrollIntoView(DiagnosticList.Items[^1]);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _service?.Dispose();
        _llm?.Dispose();
        _localNmt?.Dispose();
        _overlay?.Close();
        _cache.Save();
        _config.Save();
    }
}
