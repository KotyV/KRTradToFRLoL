using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace KRTradToFRLoL.Capture;

/// <summary>
/// Capture une zone de l'écran (pixels physiques) via BitBlt (CopyFromScreen).
/// 100 % externe au jeu : aucune interaction avec le process LoL (Vanguard-safe).
/// Suffisant à 3-5 Hz sur une petite zone ; à remplacer par Windows.Graphics.Capture en v2.
/// </summary>
public sealed class ScreenRegionCapturer
{
    public Bitmap Capture(Rectangle region)
    {
        var bmp = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(region.X, region.Y, 0, 0, region.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    /// <summary>Agrandit l'image avant OCR (le hangul du chat fait ~12-18 px).</summary>
    public static Bitmap Upscale(Bitmap src, double factor)
    {
        if (factor <= 1.01) return (Bitmap)src.Clone();
        var w = (int)(src.Width * factor);
        var h = (int)(src.Height * factor);
        var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dst);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(src, 0, 0, w, h);
        return dst;
    }
}
