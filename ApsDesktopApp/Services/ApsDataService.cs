using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ApsDesktopApp.Models;

namespace ApsDesktopApp.Services;

// Authenticated APS resource calls (identity + Data Management). WPF-free.
// All requests go through _http, whose ApsAuthHandler injects the bearer token
// and refreshes on 401, so this service never touches tokens directly -- it only
// guards on connection state via ApsAuthService for a clear "not connected" error.
public class ApsDataService
{
    private const string UserInfoUrl = "https://api.userprofile.autodesk.com/userinfo";
    private const string HubsUrl = "https://developer.api.autodesk.com/project/v1/hubs";
    private const string DataUrl = "https://developer.api.autodesk.com/data/v1";

    private readonly HttpClient _http;
    private readonly ApsAuthService _auth;

    public ApsDataService(HttpClient http, ApsAuthService auth)
    {
        _http = http;
        _auth = auth;
    }

    private void EnsureConnected()
    {
        if (!_auth.HasStoredToken)
            throw new InvalidOperationException("Not connected. Sign in first.");
    }

    // Returns null when the token is rejected (401/403 even after the handler's
    // refresh-and-retry) so callers can treat it as "signed out". Other failures
    // (network, 5xx) THROW -- they say nothing about token validity.
    public async Task<UserProfile?> GetUserProfileAsync(CancellationToken cancellationToken)
    {
        if (!_auth.HasStoredToken)
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoUrl);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
            or System.Net.HttpStatusCode.Forbidden)
            return null;
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<UserProfile>(json);
    }

    // Lists the hubs (ACC/BIM 360 accounts) visible to the signed-in user.
    // Note: the Data Management hub/project endpoints are NOT region-routed.
    public async Task<IReadOnlyList<Hub>> GetHubsAsync(CancellationToken cancellationToken)
    {
        EnsureConnected();
        var hubs = await GetJsonAsync<HubsResponse>(HubsUrl, cancellationToken);
        return hubs?.Data ?? new List<Hub>();
    }

    public async Task<IReadOnlyList<Project>> GetProjectsAsync(string hubId, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var url = $"{HubsUrl}/{Uri.EscapeDataString(hubId)}/projects";
        var projects = await GetJsonAsync<ProjectsResponse>(url, cancellationToken);
        return projects?.Data ?? new List<Project>();
    }

    // Lists a project's top-level folders. Note the base path: top folders come
    // from the project/v1 API (under the hub), but their CONTENTS come from the
    // data/v1 API (see GetFolderContentsAsync) -- two different services.
    public async Task<IReadOnlyList<FolderEntry>> GetTopFoldersAsync(
        string hubId, string projectId, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var url = $"{HubsUrl}/{Uri.EscapeDataString(hubId)}/projects/"
                  + $"{Uri.EscapeDataString(projectId)}/topFolders";
        var envelope = await GetAllPagesAsync(url, cancellationToken);
        return ExtractFolders(envelope);
    }

    // Lists one folder's contents: its subfolders and the files (items) inside
    // it, with each file's latest-version metadata resolved from "included".
    // Paginates automatically (page[limit]=200) so large folders are fully loaded.
    public async Task<FolderContents> GetFolderContentsAsync(
        string projectId, string folderId, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var url = $"{DataUrl}/projects/{Uri.EscapeDataString(projectId)}/folders/"
                  + $"{Uri.EscapeDataString(folderId)}/contents";
        var envelope = await GetAllPagesAsync(url, cancellationToken);
        return new FolderContents(ExtractFolders(envelope), ExtractFiles(envelope));
    }

    // Lists an item's full version history (newest first). Same JSON:API shape
    // as folder contents, so it reuses the ApiResource DTOs; here every "data"
    // entry is itself a "versions" resource (no "included" join needed).
    public async Task<IReadOnlyList<VersionEntry>> GetItemVersionsAsync(
        string projectId, string itemId, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var url = $"{DataUrl}/projects/{Uri.EscapeDataString(projectId)}/items/"
                  + $"{Uri.EscapeDataString(itemId)}/versions";
        var envelope = await GetAllPagesAsync(url, cancellationToken);

        var versions = new List<VersionEntry>();
        foreach (var resource in envelope.Data)
        {
            if (resource.Type != "versions")
                continue;
            var a = resource.Attributes;
            versions.Add(new VersionEntry(
                VersionNumber: a.VersionNumber ?? 0,
                FileType: a.FileType ?? string.Empty,
                SizeBytes: a.StorageSize ?? 0,
                LastModified: a.LastModifiedTime,
                ModifiedBy: a.LastModifiedUserName ?? string.Empty));
        }

        versions.Sort((x, y) => y.VersionNumber.CompareTo(x.VersionNumber));
        return versions;
    }

    // Picks the "folders" resources out of a JSON:API data array.
    private static IReadOnlyList<FolderEntry> ExtractFolders(DataApiEnvelope? response)
    {
        var folders = new List<FolderEntry>();
        if (response is null)
            return folders;

        foreach (var resource in response.Data)
        {
            if (resource.Type != "folders")
                continue;
            var name = resource.Attributes.DisplayName ?? resource.Attributes.Name ?? "(unnamed)";
            folders.Add(new FolderEntry(resource.Id, name));
        }
        return folders;
    }

    // Joins each "items" resource to its tip "versions" resource in "included"
    // (via relationships.tip.data.id) to surface the file's real metadata.
    private static IReadOnlyList<FileEntry> ExtractFiles(DataApiEnvelope? response)
    {
        var files = new List<FileEntry>();
        if (response is null)
            return files;

        var versions = new Dictionary<string, ApiResource>();
        foreach (var inc in response.Included)
        {
            if (inc.Type == "versions")
                versions[inc.Id] = inc;
        }

        foreach (var resource in response.Data)
        {
            if (resource.Type != "items")
                continue;

            var tipId = resource.Relationships?.Tip?.Data?.Id;
            var tip = tipId is not null && versions.TryGetValue(tipId, out var v) ? v : null;
            var attrs = tip?.Attributes ?? resource.Attributes;

            var name = attrs.DisplayName ?? attrs.Name
                       ?? resource.Attributes.DisplayName ?? "(unnamed)";
            files.Add(new FileEntry(
                ItemId: resource.Id,
                Name: name,
                FileType: attrs.FileType ?? string.Empty,
                VersionNumber: attrs.VersionNumber ?? 0,
                SizeBytes: attrs.StorageSize ?? 0,
                LastModified: attrs.LastModifiedTime,
                ModifiedBy: attrs.LastModifiedUserName ?? string.Empty,
                TipVersionUrn: tipId ?? string.Empty));
        }
        return files;
    }

    // Fetches all pages of a JSON:API endpoint using page[number] / page[limit]
    // pagination, accumulating data[] and included[] across pages. Follows the
    // response's links.next until absent -- the API's own termination signal --
    // rather than guessing from short pages (which would silently truncate if
    // the server ever returned a non-final page below the limit).
    private async Task<DataApiEnvelope> GetAllPagesAsync(string baseUrl, CancellationToken cancellationToken)
    {
        var accumulated = new DataApiEnvelope();
        const int pageLimit = 200;

        var separator = baseUrl.Contains('?') ? '&' : '?';
        string? url = $"{baseUrl}{separator}page[number]=0&page[limit]={pageLimit}";

        while (url is not null)
        {
            var page = await GetJsonAsync<DataApiEnvelope>(url, cancellationToken);
            if (page is null) break;

            accumulated.Data.AddRange(page.Data);
            accumulated.Included.AddRange(page.Included);

            var next = page.Links?.Next?.Href;
            // Guard against a malformed response pinning us to the same page.
            url = next is not null && next != url ? next : null;
        }

        return accumulated;
    }

    // Issues an authenticated GET and deserializes the JSON body. The bearer
    // token (and any refresh) is handled by ApsAuthHandler on _http.
    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(json);
    }
}
