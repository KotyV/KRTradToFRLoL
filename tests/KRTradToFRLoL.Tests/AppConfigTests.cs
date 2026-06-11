using KRTradToFRLoL.Core;
using KRTradToFRLoL.Security;
using Xunit;

namespace KRTradToFRLoL.Tests;

public class SecretProtectorTests
{
    [Fact]
    public void Aller_retour() =>
        Assert.Equal("sk-test-123", SecretProtector.Unprotect(SecretProtector.Protect("sk-test-123")));

    [Fact]
    public void Vide_reste_vide()
    {
        Assert.Equal("", SecretProtector.Protect(""));
        Assert.Equal("", SecretProtector.Unprotect(""));
    }

    [Fact]
    public void Valeur_corrompue_renvoie_vide_sans_lever()
    {
        Assert.Equal("", SecretProtector.Unprotect("pas-du-base64!!"));
        Assert.Equal("", SecretProtector.Unprotect(Convert.ToBase64String([1, 2, 3])));
    }
}

public class AppConfigTests
{
    private static string TempConfigPath() =>
        Path.Combine(Path.GetTempPath(), $"krtrad-config-{Guid.NewGuid():N}.json");

    [Fact]
    public void Aller_retour_disque()
    {
        var path = TempConfigPath();
        try
        {
            var config = new AppConfig { ChatRegionX = 42, ProxyUrl = "https://exemple.test/translate" };
            config.SetApiKey("sk-secret");
            config.SaveTo(path);

            var reloaded = AppConfig.LoadFrom(path);
            Assert.Equal(42, reloaded.ChatRegionX);
            Assert.Equal("https://exemple.test/translate", reloaded.ProxyUrl);
            Assert.Equal("sk-secret", reloaded.ResolveApiKey());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Aucun_secret_en_clair_sur_le_disque()
    {
        var path = TempConfigPath();
        try
        {
            var config = new AppConfig();
            config.SetApiKey("sk-secret-tres-visible");
            config.SetProxyToken("token-tres-visible");
            config.SaveTo(path);

            var raw = File.ReadAllText(path);
            Assert.DoesNotContain("sk-secret-tres-visible", raw);
            Assert.DoesNotContain("token-tres-visible", raw);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Migre_la_cle_en_clair_des_configs_v01()
    {
        var path = TempConfigPath();
        try
        {
            File.WriteAllText(path, """{ "AnthropicApiKey": "sk-ancienne-cle" }""");

            var config = AppConfig.LoadFrom(path);

            Assert.Equal("sk-ancienne-cle", config.ResolveApiKey());
            Assert.DoesNotContain("sk-ancienne-cle", File.ReadAllText(path)); // réécrite chiffrée
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Config_corrompue_revient_aux_defauts()
    {
        var path = TempConfigPath();
        try
        {
            File.WriteAllText(path, "{invalide");
            var config = AppConfig.LoadFrom(path);
            Assert.Equal(250, config.CaptureIntervalMs);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>
/// Résolution des dossiers de modèles : un bundle portable (models/ à côté de l'exe)
/// a priorité sur %AppData%, et la config explicite garde le dernier mot.
/// </summary>
public class ModelDirectoryResolutionTests
{
    private static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), $"krtrad-portable-{Guid.NewGuid():N}");

    [Fact]
    public void Prefere_le_modele_livre_a_cote_de_l_exe()
    {
        var root = TempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "ocr-ko"));
            Assert.Equal(Path.Combine(root, "ocr-ko"), AppConfig.ResolveModelDir(root, "ocr-ko"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Retombe_sur_appdata_sans_dossier_portable()
    {
        var rootAbsent = TempRoot(); // jamais créé
        Assert.Equal(
            Path.Combine(AppConfig.ConfigDir, "models", "ocr-ko"),
            AppConfig.ResolveModelDir(rootAbsent, "ocr-ko"));
    }

    [Fact]
    public void Le_dossier_portable_ne_compte_que_pour_le_modele_present()
    {
        var root = TempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "ocr-ko")); // m2m100 absent du bundle
            Assert.Equal(Path.Combine(root, "ocr-ko"), AppConfig.ResolveModelDir(root, "ocr-ko"));
            Assert.Equal(
                Path.Combine(AppConfig.ConfigDir, "models", "m2m100"),
                AppConfig.ResolveModelDir(root, "m2m100"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void La_config_explicite_garde_la_priorite()
    {
        var config = new AppConfig { LocalModelDirectory = @"D:\mes-modeles\m2m100" };
        Assert.Equal(@"D:\mes-modeles\m2m100", config.EffectiveLocalModelDirectory);
    }
}
