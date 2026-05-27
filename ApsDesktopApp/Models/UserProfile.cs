using System.Text.Json.Serialization;

namespace ApsDesktopApp.Models;

// Maps the subset we care about from the APS OIDC userinfo endpoint.
public class UserProfile
{
    [JsonPropertyName("sub")]
    public string Sub { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("picture")]
    public string? Picture { get; set; }
}
