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
// Token lifecycle only -- authenticated resource calls live in ApsDataService.
public class ApsAuthService
{
    private const string AuthorizeUrl = "https://developer.api.autodesk.com/authentication/v2/authorize";
    private const string TokenUrl = "https://developer.api.autodesk.com/authentication/v2/token";
    // offline_access is REQUIRED for APS to issue a refresh_token; without it
    // the token response has no refresh_token and EnsureValidAccessTokenAsync
    // can only ever sign out once the 1-hour access token expires.
    private const string Scopes = "data:read data:write data:create viewables:read account:read offline_access";

    // Plain client for the unauthenticated token endpoints (authorize/exchange/
    // refresh). It must NOT carry ApsAuthHandler: that handler calls back here to
    // refresh, so routing the refresh through it would recurse.
    private readonly HttpClient _http;

    private readonly TokenStorage _tokenStorage;
    private AppSettings _settings;

    // Serializes token refresh so concurrent Data Management calls that hit an
    // expired token trigger only one refresh request (APS may rotate the refresh
    // token, which would invalidate a second in-flight refresh).
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public TokenInfo? CurrentToken { get; private set; }

    public ApsAuthService(HttpClient http, TokenStorage tokenStorage)
    {
        _http = http;
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
            throw new InvalidOperationException("State mismatch -- possible CSRF. Sign-in aborted.");
        if (string.IsNullOrEmpty(callback.Code))
            throw new InvalidOperationException("No authorization code returned.");

        var token = await ExchangeCodeForTokenAsync(callback.Code, verifier, cancellationToken);
        SetToken(token);
        return token;
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
