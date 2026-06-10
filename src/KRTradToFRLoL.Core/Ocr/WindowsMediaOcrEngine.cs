using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using WinOcrEngine = Windows.Media.Ocr.OcrEngine;

namespace KRTradToFRLoL.Ocr;

/// <summary>
/// OCR natif Windows (Windows.Media.Ocr) en coréen.
/// Nécessite le pack de langue coréen : Paramètres → Heure et langue → Langue →
/// Ajouter une langue → 한국어 (la reconnaissance de texte suffit).
/// Connu pour des espaces erratiques sur le CJK : toléré, le LLM de traduction est
/// prévenu que le texte sort d'un OCR.
/// </summary>
public sealed class WindowsMediaOcrEngine : IOcrEngine
{
    private readonly WinOcrEngine? _engine;

    public WindowsMediaOcrEngine()
    {
        var korean = new Language("ko");
        if (WinOcrEngine.IsLanguageSupported(korean))
        {
            _engine = WinOcrEngine.TryCreateFromLanguage(korean);
        }
    }

    public string Name => "Windows.Media.Ocr (ko)";

    public bool IsAvailable => _engine is not null;

    public string? UnavailableReason => IsAvailable
        ? null
        : "Pack de langue coréen absent. Paramètres Windows → Langue → Ajouter « 한국어 » (cocher Reconnaissance de texte), puis relancer l'app.";

    public async Task<IReadOnlyList<string>> RecognizeLinesAsync(Bitmap image)
    {
        if (_engine is null) return Array.Empty<string>();

        using var softwareBitmap = ToSoftwareBitmap(image);
        var result = await _engine.RecognizeAsync(softwareBitmap);

        // Reconstruit chaque ligne ; l'OCR Windows segmente les "mots" CJK de façon
        // fantaisiste, on rejoint avec espace simple et on laisse le parseur/LLM gérer.
        return result.Lines
            .Select(l => string.Join(' ', l.Words.Select(w => w.Text)).Trim())
            .Where(t => t.Length > 0)
            .ToList();
    }

    private static SoftwareBitmap ToSoftwareBitmap(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * data.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var sb = new SoftwareBitmap(BitmapPixelFormat.Bgra8, bmp.Width, bmp.Height, BitmapAlphaMode.Premultiplied);
            sb.CopyFromBuffer(bytes.AsBuffer());
            return sb;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
