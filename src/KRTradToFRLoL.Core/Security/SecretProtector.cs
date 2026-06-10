using System.Security.Cryptography;
using System.Text;

namespace KRTradToFRLoL.Security;

/// <summary>
/// Chiffrement des secrets locaux (clé API, token proxy) via DPAPI Windows,
/// portée utilisateur courant : le fichier de config ne contient jamais de secret en clair,
/// et un config.json copié sur une autre machine/un autre compte est indéchiffrable.
/// </summary>
public static class SecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("KRTradToFRLoL.v1");

    /// <summary>Chiffre un secret ; renvoie une chaîne base64 stockable. Vide → vide.</summary>
    public static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return "";
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Déchiffre ; renvoie "" si la valeur est vide, corrompue ou d'une autre machine.</summary>
    public static string Unprotect(string protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64)) return "";
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedBase64), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException) { return ""; }
        catch (CryptographicException) { return ""; }
    }
}
