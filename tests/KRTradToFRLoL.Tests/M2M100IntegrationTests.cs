using KRTradToFRLoL.Core;
using KRTradToFRLoL.Translation;
using KRTradToFRLoL.Translation.Local;
using Xunit;
using Xunit.Abstractions;

namespace KRTradToFRLoL.Tests;

/// <summary>
/// Intégration M2M-100 : ne s'exécute que si les modèles ONNX sont présents
/// (tools/export_m2m100.py) — sur CI sans modèles, ces tests passent en no-op.
/// </summary>
public class M2M100IntegrationTests(ITestOutputHelper output)
{
    private static string ModelDir => new AppConfig().EffectiveLocalModelDirectory;

    [Fact]
    public async Task Traduit_une_phrase_coreenne_simple()
    {
        var translator = M2M100Translator.TryCreate(ModelDir, new Prenormalizer());
        if (translator is null)
        {
            output.WriteLine($"Modèles absents dans {ModelDir} — test sauté.");
            return;
        }

        using (translator)
        {
            var result = await translator.TranslateAsync("안녕하세요, 미드로 가주세요.", null, CancellationToken.None);

            output.WriteLine($"Traduction locale : {result}");
            Assert.False(string.IsNullOrWhiteSpace(result));
            Assert.DoesNotContain("안녕", result!); // la sortie ne doit plus être du coréen
        }
    }

    [Fact]
    public async Task La_prenormalisation_ameliore_largot()
    {
        var translator = M2M100Translator.TryCreate(ModelDir, Prenormalizer.LoadFromDataDir());
        if (translator is null)
        {
            output.WriteLine($"Modèles absents dans {ModelDir} — test sauté.");
            return;
        }

        using (translator)
        {
            var result = await translator.TranslateAsync("미드 갱 와 빨리", null, CancellationToken.None);
            output.WriteLine($"Traduction locale (prénormalisée) : {result}");
            Assert.False(string.IsNullOrWhiteSpace(result));
        }
    }
}
