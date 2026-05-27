using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ApsDesktopApp.Models;

// Maps the subset we care about from the APS Data Management "GET hubs"
// response, which follows the JSON:API envelope: { "data": [ ... ] }.
public class HubsResponse
{
    [JsonPropertyName("data")]
    public List<Hub> Data { get; set; } = new();
}

public class Hub
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("attributes")]
    public HubAttributes Attributes { get; set; } = new();

    public string Name => Attributes.Name;
}

public class HubAttributes
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}