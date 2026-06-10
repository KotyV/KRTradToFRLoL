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

    private ClaudeTranslator? _llm;
    private M2M100Translator? _localNmt;
    private TranslatorService? _service;
    private OverlayWindow? _overlay;
    private bool _editMode;

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

            _localNmt = M2M100Translator.TryCreate(_config.EffectiveLocalModelDirectory, Prenormalizer.LoadFromDataDir());
            AppendDiag(_localNmt is not null
                ? "Traduction locale M2M-100 chargée (filet hors ligne actif)."
                : $"Traduction locale absente (optionnel) — modèles attendus dans {_config.EffectiveLocalModelDirectory} (cf. tools/export_m2m100.py).");
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

    private void OnSaveKey(object sender, RoutedEventArgs e)
    {
        _config.SetApiKey(ApiKeyBox.Password);
        _config.Save();
        ApiKeyBox.Clear(); // la clé ne reste ni affichée ni en mémoire UI
        ResetServiceForConfigChange();
        SetStatus(_config.ResolveApiKey().Length > 0
            ? "Clé API enregistrée (chiffrée DPAPI). " + DescribeTranslationSetup()
            : "Clé effacée. " + DescribeTranslationSetup());
    }

    private void OnSaveProxy(object sender, RoutedEventArgs e)
    {
        _config.ProxyUrl = ProxyUrlBox.Text.Trim();
        if (ProxyTokenBox.Password.Length > 0)
            _config.SetProxyToken(ProxyTokenBox.Password);
        if (_config.ProxyUrl.Length == 0)
            _config.ProxyTokenProtected = ""; // proxy désactivé → on ne garde pas de token orphelin
        _config.Save();
        ProxyTokenBox.Clear();
        ResetServiceForConfigChange();
        SetStatus("Proxy enregistré. " + DescribeTranslationSetup());
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
            _llm = new ClaudeTranslator(_config);
            var pipeline = new TranslationPipeline(
                _glossary, _cache,
                _llm.IsConfigured ? _llm : null,
                _localNmt,
                _config.TranslationTimeoutSeconds,
                _config.LocalTranslationTimeoutSeconds);

            _service = new TranslatorService(_config, _ocr, _champions, pipeline);
            _service.StatusChanged += msg => Dispatcher.BeginInvoke(() => SetStatus(msg));
            _service.OcrFrame += lines => Dispatcher.BeginInvoke(() =>
            {
                foreach (var l in lines.Take(8)) AppendDiag($"[ocr] {l}");
            });
            _service.MessageUpdated += evt => Dispatcher.BeginInvoke(() => OnMessage(evt));
        }

        _service.Start();
        StartStopButton.Content = "■ Arrêter";
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
