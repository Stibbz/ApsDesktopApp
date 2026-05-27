using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ApsDesktopApp.Models;

// Maps the subset we care about from the APS Data Management
// "GET hubs/{hub_id}/projects" response (same JSON:API envelope as hubs).
public class ProjectsResponse
{
    [JsonPropertyName("data")]
    public List<Project> Data { get; set; } = new();
}

public class Project
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("attributes")]
    public ProjectAttributes Attributes { get; set; } = new();

    public string Name => Attributes.Name;
}

public class ProjectAttributes
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}