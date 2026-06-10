using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ApsDesktopApp.Services;

// DPAPI-encrypted storage for the Model Derivative client secret.
// Kept separate from AppSettings (plain JSON) because secrets must not be
// stored in plain text.
public class SecretStorage
{
    private readonly AppLogger? _log;

    // Logger is optional so non-DI construction (SettingsWindow) stays possible.
    public SecretStorage(AppLogger? log = null) => _log = log;

    public void Save(string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(secret);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(AppPaths.ModelDerivativeSecretFile, encrypted);
    }

    public string? Load()
    {
        var path = AppPaths.ModelDerivativeSecretFile;
        if (!File.Exists(path))
            return null;

        try
        {
            var encrypted = File.ReadAllBytes(path);
            var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            _log?.Warn("SecretStorage", $"md_secret.dat unreadable -- secret unavailable: {ex.Message}");
            return null;
        }
    }

    public bool HasSecret => File.Exists(AppPaths.ModelDerivativeSecretFile);

    public void Clear()
    {
        var path = AppPaths.ModelDerivativeSecretFile;
        if (File.Exists(path))
            File.Delete(path);
    }
}
