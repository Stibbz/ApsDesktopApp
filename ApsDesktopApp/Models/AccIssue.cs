using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ApsDesktopApp.Models;

public class AccIssueType
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}

public class AccIssuePagination
{
    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("totalResults")]
    public int TotalResults { get; set; }
}

public class AccIssue
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayId")]
    public int DisplayId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("issueType")]
    public AccIssueType? IssueType { get; set; }

    [JsonPropertyName("assignedTo")]
    public string? AssignedTo { get; set; }

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; set; }

    [JsonPropertyName("ownerId")]
    public string? OwnerId { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; }

    // Deliberately a string, unlike the sibling timestamps: the API returns a
    // date-only "yyyy-MM-dd"; parsing to DateTimeOffset would invent a midnight
    // time (and a timezone shift could move it to the wrong day).
    [JsonPropertyName("dueDate")]
    public string? DueDate { get; set; }

    [JsonPropertyName("closedAt")]
    public DateTimeOffset? ClosedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class AccIssuesResponse
{
    [JsonPropertyName("results")]
    public List<AccIssue>? Results { get; set; }

    [JsonPropertyName("pagination")]
    public AccIssuePagination? Pagination { get; set; }
}
