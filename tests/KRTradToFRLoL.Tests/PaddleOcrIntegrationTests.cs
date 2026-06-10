using System.Drawing;
using KRTradToFRLoL.Capture;
using KRTradToFRLoL.Core;
using KRTradToFRLoL.Ocr;
using Xunit;
using Xunit.Abstractions;

namespace KRTradToFRLoL.Tests;

/// <summary>
/// Intégration OCR coréen : ne s'exécute que si le modèle est présent
/// (tools/export_korean_ocr.py) — no-op sur CI sans modèle.
/// Texte rendu proprement (cas facile) : valide la mécanique segmentation + CTC + charset,
/// pas la robustesse au bruit vidéo.
/// </summary>
public class PaddleOcrIntegrationTests(ITestOutputHelper output)
{
    private static string ModelDir => new AppConfig().EffectiveOcrModelDirectory;

    private static Bitmap RenderLines(params string[] lines)
    {
        var bmp = new Bitmap(720, 20 + lines.Length * 44);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(18, 18, 24));
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        using var font = new Font("Malgun Gothic", 22, FontStyle.Regular, GraphicsUnit.Pixel);
        for (var i = 0; i < lines.Length; i++)
            g.DrawString(lines[i], font, Brushes.White, 12, 14 + i * 44);
        return bmp;
    }

    [Fact]
    public async Task Reconnait_une_ligne_de_hangul()
    {
        using var engine = PaddleKoreanOcrEngine.TryCreate(ModelDir);
        if (engine is null)
        {
            output.WriteLine($"Modèle absent dans {ModelDir} — test sauté.");
            return;
        }

        using var bmp = RenderLines("미드 갱 와주세요");
        ScreenRegionCapturer.EnhanceForOcr(bmp);

        var lines = await engine.RecognizeLinesAsync(bmp);

        output.WriteLine($"OCR : {string.Join(" | ", lines)}");
        var text = Assert.Single(lines);
        Assert.Contains("갱", text);
    }

    [Fact]
    public async Task Segmente_et_reconnait_deux_lignes()
    {
        using var engine = PaddleKoreanOcrEngine.TryCreate(ModelDir);
        if (engine is null)
        {
            output.WriteLine($"Modèle absent dans {ModelDir} — test sauté.");
            return;
        }

        using var bmp = RenderLines("[Team] 큰 곰 (Hwei): 서렌?", "바론 가자");
        ScreenRegionCapturer.EnhanceForOcr(bmp);

        var lines = await engine.RecognizeLinesAsync(bmp);

        output.WriteLine($"OCR : {string.Join(" | ", lines)}");
        Assert.Equal(2, lines.Count);
        Assert.Contains("바론", lines[1]);
    }
}
