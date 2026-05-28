using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ApsDesktopApp.Models;

public class AccProjectUser
{
    // The Admin API returns several different ID fields depending on API version.
    // Issues API assignedTo/createdBy/ownerId can match any of them -- all are
    // registered as keys so the lookup succeeds regardless of which format is used.
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("uid")]
    public string? Uid { get; set; }

    [JsonPropertyName("autodeskId")]
    public string? AutodeskId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    public string DisplayName => Name ?? Email ?? Uid ?? Id ?? string.Empty;
}

public class AccProjectUsersResponse
{
    [JsonPropertyName("results")]
    public List<AccProjectUser>? Results { get; set; }

    [JsonPropertyName("pagination")]
    public AccIssuePagination? Pagination { get; set; }
}
