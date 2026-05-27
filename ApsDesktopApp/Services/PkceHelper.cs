using System;
using System.Security.Cryptography;
using System.Text;

namespace ApsDesktopApp.Services;

// RFC 7636 PKCE helpers for the OAuth public-client flow.
public static class PkceHelper
{
    public static string CreateCodeVerifier()
    {
        // 32 random bytes -> 43-char base64url string (within the 43-128 spec range).
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    public static string CreateCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    public static string CreateState() => Base64UrlEncode(RandomNumberGenerator.GetBytes(16));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
