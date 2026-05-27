using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApsDesktopApp.Models;

namespace ApsDesktopApp.Services;

// Persists tokens encrypted with DPAPI (CurrentUser scope), so only the
// logged-in Windows account can decrypt them.
public class TokenStorage
{
    public void Save(TokenInfo token)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(token);
        var encrypted = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(AppPaths.TokensFile, encrypted);
    }

    public TokenInfo? Load()
    {
        var path = AppPaths.TokensFile;
        if (!File.Exists(path))
            return null;

        try
        {
            var encrypted = File.ReadAllBytes(path);
            var json = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<TokenInfo>(json);
        }
        catch
        {
            // Corrupt or undecryptable (e.g. copied from another machine) -> treat as no token.
            return null;
        }
    }

    public void Clear()
    {
        var path = AppPaths.TokensFile;
        if (File.Exists(path))
            File.Delete(path);
    }
}
