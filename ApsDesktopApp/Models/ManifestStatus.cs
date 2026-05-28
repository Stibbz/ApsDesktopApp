using System.Text.Json.Serialization;

namespace ApsDesktopApp.Models;

// Subset of the Model Derivative manifest response. "status" is one of
// pending/inprogress/success/failed/timeout; "progress" is a human string
// like "complete" or "57% complete".
public class ManifestStatus
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("progress")]
    public string Progress { get; set; } = string.Empty;
}
