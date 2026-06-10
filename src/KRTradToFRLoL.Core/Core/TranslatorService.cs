using System.Drawing;
using KRTradToFRLoL.Capture;
using KRTradToFRLoL.Ocr;
using KRTradToFRLoL.Parsing;
using KRTradToFRLoL.Translation;

namespace KRTradToFRLoL.Core;

public sealed record PipelineEvent(ChatMessage Message, TranslationResult? Translation, string? PartialText);

/// <summary>
/// Boucle principale : capture → changement ? → OCR → parsing → dédup → traduction.
/// Tourne sur un thread de fond ; publie les événements via <see cref="MessageUpdated"/>.
/// La chaîne de traduction est injectée (testable sans capture ni OCR).
/// </summary>
public sealed class TranslatorService(
    AppConfig config,
    IOcrEngine ocr,
    ChampionNames champions,
    TranslationPipeline pipeline) : IDisposable
{
    private readonly ScreenRegionCapturer _capturer = new();
    private readonly ChangeDetector _changeDetector = new();
    private readonly ChatFrameAssembler _assembler = new(new ChatLineParser(champions));
    private readonly Deduplicator _dedup = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private string _lastFrameText = "";
    private IReadOnlyList<ChatMessage> _lastVisible = [];

    /// <summary>Déclenché à chaque nouveau message : d'abord brut (Translation null),
    /// puis au fil du streaming (PartialText), puis avec la traduction finale.</summary>
    public event Action<PipelineEvent>? MessageUpdated;

    /// <summary>Diagnostic : lignes OCR brutes de la dernière frame analysée.</summary>
    public event Action<IReadOnlyList<string>>? OcrFrame;

    /// <summary>Messages de chat actuellement VISIBLES à l'écran (à chaque frame) :
    /// permet à l'overlay de disparaître en même temps que le chat (fade/scroll).</summary>
    public event Action<IReadOnlyList<ChatMessage>>? VisibleFrame;

    public event Action<string>? StatusChanged;

    public bool IsRunning => _loop is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _dedup.Reset();
        _lastFrameText = "";
        _loop = Task.Run(() => RunLoopAsync(_cts.Token));
        StatusChanged?.Invoke("Capture démarrée.");
    }

    public void Stop()
    {
        _cts?.Cancel();
        StatusChanged?.Invoke("Capture arrêtée.");
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var started = Environment.TickCount64;
            try
            {
                await ProcessOneFrameAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // Robustesse broadcast : une frame qui échoue (fenêtre perdue, device GDI...)
                // ne tue jamais la boucle — on journalise et on continue.
                StatusChanged?.Invoke($"Erreur pipeline (auto-recovery) : {ex.Message}");
            }

            var elapsed = Environment.TickCount64 - started;
            var wait = Math.Max(30, config.CaptureIntervalMs - (int)elapsed);
            try { await Task.Delay((int)wait, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task ProcessOneFrameAsync(CancellationToken ct)
    {
        var region = new Rectangle(config.ChatRegionX, config.ChatRegionY,
            config.ChatRegionWidth, config.ChatRegionHeight);
        if (region.Width < 40 || region.Height < 20) return;

        using var raw = _capturer.Capture(region);
        if (!_changeDetector.HasChanged(raw)) return;

        using var upscaled = ScreenRegionCapturer.Upscale(raw, config.OcrUpscaleFactor);
        ScreenRegionCapturer.EnhanceForOcr(upscaled);
        var lines = await ocr.RecognizeLinesAsync(upscaled);

        // Le diff de pixels ne suffit pas : le jeu bouge en permanence derrière le chat
        // semi-transparent. Second rideau : si le TEXTE reconnu n'a pas changé, rien à faire —
        // mais on republie la visibilité pour que l'overlay continue de suivre le chat.
        var frameText = string.Join('\n', lines);
        if (frameText == _lastFrameText)
        {
            VisibleFrame?.Invoke(_lastVisible);
            return;
        }
        _lastFrameText = frameText;

        OcrFrame?.Invoke(lines);

        var visible = _assembler.Assemble(lines, config.MirrorAllLines);
        _lastVisible = visible;
        VisibleFrame?.Invoke(visible);

        foreach (var msg in visible)
        {
            ct.ThrowIfCancellationRequested();
            if (!_dedup.IsNew(msg)) continue;

            // Message sans hangul (anglais…) : copie telle quelle, pas de traduction.
            if (!msg.NeedsTranslation)
            {
                MessageUpdated?.Invoke(new PipelineEvent(msg, new TranslationResult(msg.Text, "copie"), null));
                continue;
            }

            // Affiche immédiatement le coréen brut ; la traduction le remplacera.
            MessageUpdated?.Invoke(new PipelineEvent(msg, null, null));

            // Traduction en parallèle : la boucle de capture ne s'arrête jamais.
            _ = Task.Run(async () =>
            {
                var result = await pipeline.TranslateAsync(
                    msg.Text,
                    partial => MessageUpdated?.Invoke(new PipelineEvent(msg, null, partial)),
                    ct);
                MessageUpdated?.Invoke(new PipelineEvent(msg, result, null));
            }, ct);
        }
    }

    public void Dispose() => _cts?.Cancel();
}
