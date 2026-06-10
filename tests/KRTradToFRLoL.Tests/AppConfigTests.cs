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
