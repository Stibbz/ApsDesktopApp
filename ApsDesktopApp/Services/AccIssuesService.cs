using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ApsDesktopApp.Models;

namespace ApsDesktopApp.Services;

// Wraps ACC Issues API v2. Uses the "data" 3-legged HttpClient -- issues require
// user context. The projectId here is the DM project ID with the prefix stripped
// (b.{uuid} -> {uuid}) as required by the Issues API.
public class AccIssuesService
{
    private const string BaseUrl = "https://developer.api.autodesk.com/construction/issues/v1";
    private const int PageSize = 100;
    private const string LogCategory = "AccIssues";

    private readonly HttpClient _http;
    private readonly ApsAuthService _auth;
    private readonly AppLogger _log;

    public AccIssuesService(HttpClient http, ApsAuthService auth, AppLogger log)
    {
        _http = http;
        _auth = auth;
        _log = log;
    }

    // Fetches every issue page-by-page, reporting (loaded, total) as each page arrives.
    public async Task<List<AccIssue>> GetAllIssuesAsync(
        string projectId,
        IProgress<(int loaded, int total)>? progress,
        CancellationToken ct)
    {
        EnsureConnected();
        var pid = StripPrefix(projectId);
        if (!Guid.TryParse(pid, out _))
            throw new InvalidOperationException(
                "Issues are only available for ACC projects. This project appears to be a BIM360 legacy project.");
        var baseUrl = $"{BaseUrl}/projects/{Uri.EscapeDataString(pid)}/issues";

        var all = new List<AccIssue>();
        int offset = 0;
        int total = -1;

        while (true)
        {
            var paged = $"{baseUrl}?offset={offset}&limit={PageSize}";
            var response = await GetJsonAsync<AccIssuesResponse>(paged, ct);

            if (response?.Results is null || response.Results.Count == 0) break;
            all.AddRange(response.Results);

            if (total < 0 && response.Pagination is not null)
                total = response.Pagination.TotalResults;

            offset += response.Results.Count;
            progress?.Report((all.Count, total < 0 ? 0 : total));
            _log.Debug(LogCategory, $"Page loaded: {all.Count}/{(total < 0 ? "?" : total)} issues");

            if (response.Results.Count < PageSize) break;

            // Small pause to avoid hammering the rate limiter on large projects.
            await Task.Delay(100, ct);
        }

        _log.Info(LogCategory, $"Loaded {all.Count} issues for project {pid}");
        return all;
    }

    // Sends a PATCH to update a single issue. Fields dict maps API field names to
    // new values; null values clear the field. Throws on non-2xx.
    public async Task PatchIssueAsync(
        string projectId,
        string issueId,
        Dictionary<string, object?> fields,
        CancellationToken ct)
    {
        EnsureConnected();
        var pid = StripPrefix(projectId);
        var url = $"{BaseUrl}/projects/{Uri.EscapeDataString(pid)}/issues/{Uri.EscapeDataString(issueId)}";

        var json = JsonSerializer.Serialize(fields);
        _log.Debug(LogCategory, $"PATCH issue {issueId}: {json}");

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
        {
            Content = content
        };
        using var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _log.Error(LogCategory, $"PATCH {issueId} failed {(int)response.StatusCode}: {body}");
            throw new HttpRequestException(
                $"Issue update failed ({(int)response.StatusCode}): {body}");
        }
    }

    private static string ExtractApsError(string body, int statusCode)
    {
        try
        {
            var doc  = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var code = root.TryGetProperty("errorCode", out var ec) ? ec.GetString() : null;
            var title = root.TryGetProperty("title", out var t) ? t.GetString() : null;

            if (code == "ISSUES_SERVICE_NOT_FOUND")
                return "Issues module is not enabled for this project. Enable it in ACC Admin first.";
            if (!string.IsNullOrEmpty(title))
                return $"APS error ({statusCode}): {title}";
        }
        catch { }
        return $"HTTP {statusCode}";
    }

    // Strips the "b." / "a." Data Management prefix -- Issues API expects raw UUID.
    private static string StripPrefix(string projectId)
    {
        var dot = projectId.IndexOf('.');
        return dot >= 0 ? projectId[(dot + 1)..] : projectId;
    }

    private void EnsureConnected()
    {
        if (!_auth.HasStoredToken)
            throw new InvalidOperationException("Not connected. Sign in first.");
    }

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _log.Error(LogCategory, $"GET {url} -> {(int)response.StatusCode}: {body}");
            var friendly = ExtractApsError(body, (int)response.StatusCode);
            throw new HttpRequestException(friendly);
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json);
    }
}
