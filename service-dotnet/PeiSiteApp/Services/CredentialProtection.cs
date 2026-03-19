using System.Security.Cryptography;
using System.Text;

namespace PeiSiteApp.Services;

/// <summary>
/// Wraps Windows DPAPI to encrypt/decrypt sensitive config values (e.g. API key) at rest.
/// Uses LocalMachine scope so both the Windows Service and the WPF app can read the same value.
/// </summary>
public static class CredentialProtection
{
    public static string Protect(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.LocalMachine);
        return Convert.ToBase64String(encrypted);
    }

    public static string Unprotect(string base64Ciphertext)
    {
        var encrypted = Convert.FromBase64String(base64Ciphertext);
        var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.LocalMachine);
        return Encoding.UTF8.GetString(bytes);
    }

    public static string? TryUnprotect(string base64Ciphertext)
    {
        try { return Unprotect(base64Ciphertext); }
        catch { return null; }
    }
}
