using System.Drawing;

namespace KRTradToFRLoL.Ocr;

/// <summary>
/// Abstraction du moteur OCR : permet de remplacer Windows.Media.Ocr par
/// PaddleOCR-coréen (ONNX) si la qualité est insuffisante sur le chat réel.
/// </summary>
public interface IOcrEngine
{
    /// <summary>Nom lisible du moteur (diagnostic).</summary>
    string Name { get; }

    /// <summary>Vrai si le moteur est prêt (langue coréenne disponible…).</summary>
    bool IsAvailable { get; }

    /// <summary>Message d'aide si IsAvailable est faux.</summary>
    string? UnavailableReason { get; }

    /// <summary>Reconnaît le texte d'une image et renvoie les lignes (haut → bas).</summary>
    Task<IReadOnlyList<string>> RecognizeLinesAsync(Bitmap image);
}
