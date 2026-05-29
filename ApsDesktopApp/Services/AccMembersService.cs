using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ApsDesktopApp.Models;

namespace ApsDesktopApp.Services;

// Fetches ACC project members and builds a userId->name lookup for the Issues tool.
// Endpoint: GET /construction/admin/v1/projects/{projectId}/users (no account ID in path).
// All known user ID fields are registered as keys so whatever format Issues uses hits.
public class AccMembersService
{
    private const string AdminBase = "https://developer.api.autodesk.com/construction/admin/v1";
    private const string LogCategory = "AccMembers";

    private readonly HttpClient     _http;
    private readonly ApsAuthService _auth;
    private readonly AppLogger      _log;

    public AccMembersService(HttpClient http, ApsAuthService auth, AppLogger log)
    {
        _http = http;
        _auth = auth;
        _log  = log;
    }

    // projectId: DM project ID (b.{uuid} or plain uuid -- prefix is stripped).
    // Returns id->name. Empty dict on any API failure (shown in logs).
    public async Task<Dictionary<string, string>> GetMemberLookupAsync(
        string projectId, CancellationToken ct)
    {
        if (!_auth.HasStoredToken) return [];

        var pid    = StripPrefix(projectId);
        var url    = $"{AdminBase}/projects/{Uri.EscapeDataString(pid)}/users";
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int offset = 0;

        _log.Info(LogCategory, $"Fetching members: {url}");

        while (true)
        {
            try
            {
                var paged    = $"{url}?limit=100&offset={offset}";
                var response = await GetJsonAsync<AccProjectUsersResponse>(paged, ct);

                if (response?.Results is null || response.Results.Count == 0) break;

                foreach (var user in response.Results)
                {
                    var name = user.DisplayName;
                    TryRegister(lookup, user.Uid,        name);
                    TryRegister(lookup, user.AutodeskId, name);
                    TryRegister(lookup, user.Id,         name);
                    TryRegister(lookup, user.Email,      name);
                }

                offset += response.Results.Count;
                _log.Debug(LogCategory, $"Page loaded: {offset} members so far");
                if (response.Results.Count < 100) break;
            }
            catch (HttpRequestException ex)
            {
                _log.Warn(LogCategory, $"Member API call failed: {ex.Message}");
                break;
            }
            catch (Exception ex)
            {
                _log.Warn(LogCategory, $"Member fetch stopped: {ex.Message}");
                break;
            }
        }

        _log.Info(LogCategory, $"Member lookup ready: {lookup.Count} key(s) across {offset} member(s)");
        return lookup;
    }

    private static void TryRegister(Dictionary<string, string> lookup, string? key, string name)
    {
        if (!string.IsNullOrWhiteSpace(key))
            lookup.TryAdd(key, name);
    }

    private static string StripPrefix(string id)
    {
        var dot = id.IndexOf('.');
        return dot >= 0 ? id[(dot + 1)..] : id;
    }

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        using var request  = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json);
    }
}
