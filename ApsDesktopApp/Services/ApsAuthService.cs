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
    private const string Scopes = "data:read data:write data:create viewables:read";

    private readonly HttpClient _http;
    private readonly TokenStorage _tokenStorage;
    private AppSettings _settings;

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

        using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoUrl);
        request.Headers.Add("Authorization", $"Bearer {CurrentToken.AccessToken}");

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<UserProfile>(json);
    }

    public void SignOut()
    {
        CurrentToken = null;
        _tokenStorage.Clear();
    }

    // -----------------------------------------------------------------------
    // TODO(you): implement the token-refresh strategy.
    // Decided behaviour: on refresh failure, sign out and return null so the
    // UI treats it as "not connected" and prompts re-login.
    //
    //   1. if CurrentToken is null            -> return null
    //   2. if !CurrentToken.IsExpired         -> return CurrentToken.AccessToken
    //   3. otherwise try:
    //        var refreshed = await RefreshTokenAsync(CurrentToken.RefreshToken, cancellationToken);
    //        SetToken(refreshed);
    //        return refreshed.AccessToken;
    //      catch -> SignOut(); return null;
    public async Task<string?> EnsureValidAccessTokenAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException("Implement the refresh strategy here.");
    }
    // -----------------------------------------------------------------------

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
