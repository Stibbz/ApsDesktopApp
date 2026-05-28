using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ApsDesktopApp.Models;

// --- Raw JSON:API DTOs ----------------------------------------------------
// Both the "topFolders" and "folder contents" responses use the JSON:API
// envelope: { "data": [ ... ], "included": [ ... ] }. The contents response
// mixes folders and items in "data" (tell them apart via "type"), and a file's
// real metadata lives in the "included" array as a "versions" resource that the
// item points at through relationships.tip.data.id.

public class FolderContentsResponse
{
    [JsonPropertyName("data")]
    public List<ApiResource> Data { get; set; } = new();

    [JsonPropertyName("included")]
    public List<ApiResource> Included { get; set; } = new();
}

public class ApiResource
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("attributes")]
    public ResourceAttributes Attributes { get; set; } = new();

    [JsonPropertyName("relationships")]
    public ResourceRelationships? Relationships { get; set; }
}

public class ResourceAttributes
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("fileType")]
    public string? FileType { get; set; }

    [JsonPropertyName("versionNumber")]
    public int? VersionNumber { get; set; }

    [JsonPropertyName("storageSize")]
    public long? StorageSize { get; set; }

    [JsonPropertyName("lastModifiedTime")]
    public DateTimeOffset? LastModifiedTime { get; set; }

    [JsonPropertyName("lastModifiedUserName")]
    public string? LastModifiedUserName { get; set; }
}

public class ResourceRelationships
{
    [JsonPropertyName("tip")]
    public Relationship? Tip { get; set; }
}

public class Relationship
{
    [JsonPropertyName("data")]
    public ResourceRef? Data { get; set; }
}

public class ResourceRef
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

// --- Friendly projections -------------------------------------------------
// What the service hands back after flattening the JSON:API envelope. These
// stay WPF-free (Models layer); the ViewModels wrap them for the tree/grid.

public record FolderEntry(string Id, string Name);

public record FileEntry(
    string ItemId,
    string Name,
    string FileType,
    int VersionNumber,
    long SizeBytes,
    DateTimeOffset? LastModified,
    string ModifiedBy,
    string TipVersionUrn);

// Folders and files in one directory, as returned by a single contents call.
public record FolderContents(
    IReadOnlyList<FolderEntry> Folders,
    IReadOnlyList<FileEntry> Files);

// One entry from an item's version history (GET .../items/{id}/versions). The
// versions response is the same JSON:API shape, so it reuses ApiResource DTOs.
public record VersionEntry(
    int VersionNumber,
    string FileType,
    long SizeBytes,
    DateTimeOffset? LastModified,
    string ModifiedBy);
