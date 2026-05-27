using System;
using System.Text.Json.Serialization;

namespace ApsDesktopApp.Models;

public class TokenInfo
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    // Set locally when the token is received; not part of the APS response.
    [JsonPropertyName("obtained_at")]
    public DateTimeOffset ObtainedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public DateTimeOffset ExpiresAt => ObtainedAt.AddSeconds(ExpiresIn);

    // Treat as expired one minute early to avoid edge-of-expiry failures.
    [JsonIgnore]
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt.AddMinutes(-1);
}
