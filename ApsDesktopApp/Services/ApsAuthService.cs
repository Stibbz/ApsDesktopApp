using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using ApsDesktopApp.Models;

namespace ApsDesktopApp.Services;

// Three-legged OAuth 2.0 with PKCE against Autodesk APS (Authentication API v2).
public class ApsAuthService
{
    private const string AuthorizeUrl = "https://developer.api.autodesk.com/authentication/v2/authorize";
    private const string TokenUrl = "https://developer.api.autodesk.com/authentication/v2/token";
    private const string UserInfoUrl = "https://api.userprofile.autodesk.com/userinfo";
    private const string HubsUrl = "https://developer.api.autodesk.com/project/v1/hubs";
    private const string DataUrl = "https://developer.api.autodesk.com/data/v1";
    // offline_access is REQUIRED for APS to issue a refresh_token; without it
    // the token response has no refresh_token and EnsureValidAccessTokenAsync
    // can only ever sign out once the 1-hour access token expires.
    private const string Scopes = "data:read data:write data:create viewables:read offline_access";

    // Plain client for the unauthenticated token endpoints (authorize/exchange/
    // refresh). Kept separate from _dataHttp to avoid recursion: _dataHttp's
    // ApsAuthHandler calls back into this service to refresh, and that refresh
    // must NOT go through the handler.
    private readonly HttpClient _http;

    // Client whose ApsAuthHandler injects the bearer token (and retries once on
    // 401). All authenticated Data Management / userinfo calls use this.
    private readonly HttpClient _dataHttp;

    private readonly TokenStorage _tokenStorage;
    private AppSettings _settings;

    // Serializes token refresh so concurrent Data Management calls that hit an
    // expired token trigger only one refresh request (APS may rotate the refresh
    // token, which would invalidate a second in-flight refresh).
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public TokenInfo? CurrentToken { get; private set; }

    public ApsAuthService(HttpClient http, HttpClient dataHttp, TokenStorage tokenStorage)
    {
        _http = http;
        _dataHttp = dataHttp;
        _tokenStorage = tokenStorage;
        _settings = AppSettings.Load();
        CurrentToken = _tokenStorage.Load();
    }

    public void ReloadSettings() => _settings = AppSettings.Load();
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.ClientId);
    public bool HasStoredToken => CurrentToken is not null;

    // Launches the browser, captures the redirect, and exchanges the code for tokens.
    public async Task<TokenInfo> SignInAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("APS Client ID is not configured. Open Settings first.");

        var verifier = PkceHelper.CreateCodeVerifier();
        var challenge = PkceHelper.CreateCodeChallenge(verifier);
        var expectedState = PkceHelper.CreateState();

        var server = new OAuthCallbackServer(_settings.CallbackPort);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));

        var callbackTask = server.WaitForCallbackAsync(timeout.Token);

        OpenBrowser(BuildAuthorizeUrl(challenge, expectedState));

        var callback = await callbackTask;

        if (!string.IsNullOrEmpty(callback.Error))
            throw new InvalidOperationException($"Authorization failed: {callback.Error}");
        if (callback.State != expectedState)
            throw new InvalidOperationException("State mismatch — possible CSRF. Sign-in aborted.");
        if (string.IsNullOrEmpty(callback.Code))
            throw new InvalidOperationException("No authorization code returned.");

        var token = await ExchangeCodeForTokenAsync(callback.Code, verifier, cancellationToken);
        SetToken(token);
        return token;
    }

    public async Task<UserProfile?> GetUserProfileAsync(CancellationToken cancellationToken)
    {
        if (CurrentToken is null)
            return null;

        // Bearer token is injected by ApsAuthHandler on _dataHttp.
        using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoUrl);

        using var response = await _dataHttp.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<UserProfile>(json);
    }

    // Lists the hubs (ACC/BIM 360 accounts) visible to the signed-in user.
    // Requires the data:read scope; a successful call proves the connection
    // works end-to-end against the Data Management API, not just identity.
    public async Task<IReadOnlyList<Hub>> GetHubsAsync(CancellationToken cancellationToken)
    {
        if (CurrentToken is null)
            throw new InvalidOperationException("Not connected. Sign in first.");

        // Note: the Data Management hub/project endpoints are NOT region-routed
        // (region applies to Model Derivative/OSS), so we don't pass a region
        // query param here -- it would be ignored. The _settings.Region value is
        // kept for those other APIs as the app grows.
        var hubs = await GetJsonAsync<HubsResponse>(HubsUrl, cancellationToken);
        return hubs?.Data ?? new List<Hub>();
    }

    // Lists the projects inside a hub. A successful call is the clearest proof
    // the connection works -- the returned names are recognizable projects.
    public async Task<IReadOnlyList<Project>> GetProjectsAsync(string hubId, CancellationToken cancellationToken)
    {
        if (CurrentToken is null)
            throw new InvalidOperationException("Not connected. Sign in first.");

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
        if (CurrentToken is null)
            throw new InvalidOperationException("Not connected. Sign in first.");

        var url = $"{HubsUrl}/{Uri.EscapeDataString(hubId)}/projects/"
                  + $"{Uri.EscapeDataString(projectId)}/topFolders";
        var response = await GetJsonAsync<FolderContentsResponse>(url, cancellationToken);
        return ExtractFolders(response);
    }

    // Lists one folder's contents: its subfolders and the files (items) inside
    // it, with each file's latest-version metadata resolved from the response's
    // "included" array.
    public async Task<FolderContents> GetFolderContentsAsync(
        string projectId, string folderId, CancellationToken cancellationToken)
    {
        if (CurrentToken is null)
            throw new InvalidOperationException("Not connected. Sign in first.");

        var url = $"{DataUrl}/projects/{Uri.EscapeDataString(projectId)}/folders/"
                  + $"{Uri.EscapeDataString(folderId)}/contents";
        var response = await GetJsonAsync<FolderContentsResponse>(url, cancellationToken);

        return new FolderContents(ExtractFolders(response), ExtractFiles(response));
    }

    // Picks the "folders" resources out of a JSON:API data array.
    private static IReadOnlyList<FolderEntry> ExtractFolders(FolderContentsResponse? response)
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
    private static IReadOnlyList<FileEntry> ExtractFiles(FolderContentsResponse? response)
    {
        var files = new List<FileEntry>();
        if (response is null)
            return files;

        // Index the included version resources by id for O(1) lookups.
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
                Name: name,
                FileType: attrs.FileType ?? string.Empty,
                VersionNumber: attrs.VersionNumber ?? 0,
                SizeBytes: attrs.StorageSize ?? 0,
                LastModified: attrs.LastModifiedTime,
                ModifiedBy: attrs.LastModifiedUserName ?? string.Empty));
        }
        return files;
    }

    // Issues an authenticated GET and deserializes the JSON body. The bearer
    // token (and any refresh) is handled by ApsAuthHandler on _dataHttp.
    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        using var response = await _dataHttp.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(json);
    }

    public void SignOut()
    {
        CurrentToken = null;
        _tokenStorage.Clear();
    }

    // Returns a non-expired access token, refreshing if needed. On refresh
    // failure we sign out and return null so the UI treats it as "not connected"
    // and prompts re-login. Single choke point for all Data Management calls.
    //
    // forceRefresh: refresh even if the token looks valid by the clock. Used by
    // ApsAuthHandler when the server rejects a clock-valid token with a 401.
    public async Task<string?> EnsureValidAccessTokenAsync(
        CancellationToken cancellationToken, bool forceRefresh = false)
    {
        // Fast path: no token at all -> caller is "not connected".
        if (CurrentToken is null)
            return null;

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check under the lock: a queued caller may find the token
            // already signed out (refresh failed) or already refreshed (still
            // valid) by whoever held the lock first.
            if (CurrentToken is null)
                return null;
            if (!forceRefresh && !CurrentToken.IsExpired)
                return CurrentToken.AccessToken;

            try
            {
                var refreshed = await RefreshTokenAsync(CurrentToken.RefreshToken, cancellationToken);
                SetToken(refreshed);
                return refreshed.AccessToken;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A genuine refresh failure (bad/expired refresh token, network)
                // means re-login. Cancellation is NOT a failure -- let it
                // propagate without signing the user out.
                SignOut();
                return null;
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<TokenInfo> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = _settings.ClientId,
            ["scope"] = Scopes,
        };

        using var response = await _http.PostAsync(TokenUrl, new FormUrlEncodedContent(form), cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<TokenInfo>(json)
            ?? throw new InvalidOperationException("Empty token response from APS.");
    }

    private async Task<TokenInfo> ExchangeCodeForTokenAsync(string code, string verifier, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = _settings.ClientId,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = _settings.RedirectUri,
        };

        using var response = await _http.PostAsync(TokenUrl, new FormUrlEncodedContent(form), cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<TokenInfo>(json)
            ?? throw new InvalidOperationException("Empty token response from APS.");
    }

    private void SetToken(TokenInfo token)
    {
        CurrentToken = token;
        _tokenStorage.Save(token);
    }

    private string BuildAuthorizeUrl(string codeChallenge, string state)
    {
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["response_type"] = "code";
        query["client_id"] = _settings.ClientId;
        query["redirect_uri"] = _settings.RedirectUri;
        query["scope"] = Scopes;
        query["state"] = state;
        query["code_challenge"] = codeChallenge;
        query["code_challenge_method"] = "S256";
        return $"{AuthorizeUrl}?{query}";
    }

    private static void OpenBrowser(string url)
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
}
