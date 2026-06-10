using KRTradToFRLoL.Translation;
using Xunit;

namespace KRTradToFRLoL.Tests;

public class TranslationCacheTests
{
    [Fact]
    public void Get_normalise_comme_put()
    {
        var cache = new TranslationCache();
        cache.Put("미드 미아", "mid mia");

        Assert.Equal("mid mia", cache.Get(" 미드  미아 "));
    }

    [Fact]
    public void Inconnu_renvoie_null()
    {
        Assert.Null(new TranslationCache().Get("뭐"));
    }

    [Fact]
    public void Persistance_aller_retour()
    {
        var path = Path.Combine(Path.GetTempPath(), $"krtrad-test-{Guid.NewGuid():N}.json");
        try
        {
            var cache = TranslationCache.LoadFrom(path);
            cache.Put("문장", "phrase");
            cache.Save();

            var reloaded = TranslationCache.LoadFrom(path);
            Assert.Equal("phrase", reloaded.Get("문장"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Fichier_corrompu_repart_a_vide()
    {
        var path = Path.Combine(Path.GetTempPath(), $"krtrad-test-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{pas du json");
            var cache = TranslationCache.LoadFrom(path);
            Assert.Null(cache.Get("뭐"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Cache_memoire_seule_save_est_sans_effet()
    {
        var cache = new TranslationCache();
        cache.Put("문장", "phrase");
        cache.Save(); // ne doit pas lever
        Assert.Equal("phrase", cache.Get("문장"));
    }
}
