using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ApsDesktopApp.Models;

// Subset of the Model Derivative manifest response. "status" is one of
// pending / inprogress / success / failed / timeout.
public class ManifestStatus
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("progress")]
    public string Progress { get; set; } = string.Empty;

    [JsonPropertyName("derivatives")]
    public List<ManifestDerivative>? Derivatives { get; set; }
}

// One output format entry inside the manifest (e.g. type="ifc", status="success").
public class ManifestDerivative
{
    [JsonPropertyName("outputType")]
    public string OutputType { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("children")]
    public List<ManifestChild>? Children { get; set; }
}

// A single downloadable resource within a derivative (the file itself).
public class ManifestChild
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("urn")]
    public string Urn { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string? Role { get; set; }
}
