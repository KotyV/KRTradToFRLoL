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

    /// <summary>
    /// Niveaux de gris + étirement de contraste (percentiles 2-98) : le texte clair du chat
    /// ressort du fond de jeu texturé, sans binarisation dure (le fond est animé).
    /// Aide tous les moteurs OCR sur le hangul 12-18 px.
    /// </summary>
    public static unsafe void EnhanceForOcr(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var histogram = new int[256];
            var ptr = (byte*)data.Scan0;
            var total = bmp.Width * bmp.Height;

            for (var y = 0; y < bmp.Height; y++)
            {
                var row = ptr + y * data.Stride;
                for (var x = 0; x < bmp.Width; x++)
                {
                    var p = row + x * 4;
                    var luma = (p[2] * 299 + p[1] * 587 + p[0] * 114) / 1000;
                    histogram[luma]++;
                }
            }

            int lo = 0, hi = 255, acc = 0;
            var cut = total / 50; // 2 %
            for (var i = 0; i < 256; i++) { acc += histogram[i]; if (acc >= cut) { lo = i; break; } }
            acc = 0;
            for (var i = 255; i >= 0; i--) { acc += histogram[i]; if (acc >= cut) { hi = i; break; } }
            var range = Math.Max(1, hi - lo);

            for (var y = 0; y < bmp.Height; y++)
            {
                var row = ptr + y * data.Stride;
                for (var x = 0; x < bmp.Width; x++)
                {
                    var p = row + x * 4;
                    var luma = (p[2] * 299 + p[1] * 587 + p[0] * 114) / 1000;
                    var stretched = (byte)Math.Clamp((luma - lo) * 255 / range, 0, 255);
                    p[0] = p[1] = p[2] = stretched;
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
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
