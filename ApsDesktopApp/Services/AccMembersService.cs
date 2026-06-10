using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ApsDesktopApp.Models;

namespace ApsDesktopApp.Services;

// Outcome of a member-lookup fetch. IsComplete=false means the lookup is
// partial (or empty) because the API failed -- callers should tell the user
// instead of silently rendering raw IDs.
public record MemberLookupResult(
    Dictionary<string, string> Lookup,
    bool IsComplete,
    string? Error);

// Fetches ACC project members and builds a userId->name lookup for the Issues tool.
// Endpoint: GET /construction/admin/v1/projects/{projectId}/users (no account ID in path).
// All known user ID fields are registered as keys so whatever format Issues uses hits.
public class AccMembersService
{
    private const string AdminBase = "https://developer.api.autodesk.com/construction/admin/v1";
    private const string LogCategory = "AccMembers";
    private const int PageSize = 200; // API max; default is only 20

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
    // Never throws (except cancellation): failures are reported via the result
    // so the Issues load can proceed with raw IDs and a visible warning.
    public async Task<MemberLookupResult> GetMemberLookupAsync(
        string projectId, CancellationToken ct)
    {
        if (!_auth.HasStoredToken)
            return new MemberLookupResult([], false, "Not connected.");

        var pid    = StripPrefix(projectId);
        var url    = $"{AdminBase}/projects/{Uri.EscapeDataString(pid)}/users";
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int offset = 0;
        int total  = -1;

        _log.Info(LogCategory, $"Fetching members: {url}");

        try
        {
            while (true)
            {
                // fields= trims the payload to just what the lookup needs.
                var paged    = $"{url}?limit={PageSize}&offset={offset}"
                             + "&fields=id,uid,autodeskId,email,name";
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

                if (total < 0 && response.Pagination is not null)
                    total = response.Pagination.TotalResults;

                offset += response.Results.Count;
                _log.Debug(LogCategory, $"Page loaded: {offset} members so far");

                // Terminate on the API's totalResults; short page is a fallback.
                if (total >= 0 && offset >= total) break;
                if (total < 0 && response.Results.Count < PageSize) break;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Missing account:read scope (APS answers 404), network error, or a
            // mid-pagination failure. Return what we have, flagged as partial.
            _log.Warn(LogCategory, $"Member fetch failed after {offset} member(s): {ex.Message}");
            return new MemberLookupResult(lookup, false, ex.Message);
        }

        _log.Info(LogCategory, $"Member lookup ready: {lookup.Count} key(s) across {offset} member(s)");
        return new MemberLookupResult(lookup, true, null);
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
