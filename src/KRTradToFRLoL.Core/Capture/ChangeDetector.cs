using System.Drawing;
using System.Drawing.Imaging;

namespace KRTradToFRLoL.Capture;

/// <summary>
/// Détecte si la zone capturée a changé depuis la dernière frame (hash échantillonné),
/// pour ne lancer l'OCR que lorsque c'est utile.
/// </summary>
public sealed class ChangeDetector
{
    private ulong _lastHash;

    public bool HasChanged(Bitmap bmp)
    {
        var hash = ComputeSampledHash(bmp);
        if (hash == _lastHash) return false;
        _lastHash = hash;
        return true;
    }

    private static unsafe ulong ComputeSampledHash(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            ulong hash = 14695981039346656037UL; // FNV-1a
            var ptr = (byte*)data.Scan0;
            // Échantillonne 1 pixel sur 4 en X et 1 ligne sur 2 en Y : largement assez
            // pour détecter une nouvelle ligne de chat, et ~8x moins de travail.
            for (var y = 0; y < bmp.Height; y += 2)
            {
                var row = ptr + y * data.Stride;
                for (var x = 0; x < bmp.Width; x += 4)
                {
                    var px = *(uint*)(row + x * 4) & 0x00FFFFFF;
                    hash = (hash ^ px) * 1099511628211UL;
                }
            }
            return hash;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
