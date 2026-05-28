using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ApsDesktopApp.Models;

namespace ApsDesktopApp.Services;

// Fetches and caches a 2-legged (client credentials) APS token for the
// Model Derivative server-side app. Re-fetches automatically on expiry.
// Uses the plain HttpClient (no auth handler) -- the token endpoint itself
// must not carry a bearer token.
public class TwoLeggedTokenService
{
    private const string TokenUrl = "https://developer.api.autodesk.com/authentication/v2/token";
    private const string Scopes = "data:read data:write viewables:read";

    private readonly HttpClient _http;
    private readonly SecretStorage _secretStorage;
    private AppSettings _settings;

    private TokenInfo? _cached;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public TwoLeggedTokenService(HttpClient http, SecretStorage secretStorage)
    {
        _http = http;
        _secretStorage = secretStorage;
        _settings = AppSettings.Load();
    }

    public void ReloadSettings()
    {
        _settings = AppSettings.Load();
        _cached = null; // new client ID means any cached token is stale
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settings.ModelDerivativeClientId)
        && _secretStorage.HasSecret;

    // Returns a valid access token, fetching or re-using the cached one.
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cached is not null && !_cached.IsExpired)
                return _cached.AccessToken;

            _cached = await FetchAsync(cancellationToken);
            return _cached.AccessToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    // Forces the next GetTokenAsync to re-fetch, used by TwoLeggedAuthHandler
    // when the server rejects a token that looks valid by the clock.
    public void Invalidate() => _cached = null;

    private async Task<TokenInfo> FetchAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ModelDerivativeClientId))
            throw new InvalidOperationException(
                "Model Derivative Client ID not configured. Open Settings first.");

        var secret = _secretStorage.Load();
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException(
                "Model Derivative Client Secret not configured. Open Settings first.");

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _settings.ModelDerivativeClientId,
            ["client_secret"] = secret,
            ["scope"] = Scopes,
        };

        using var response = await _http.PostAsync(
            TokenUrl, new FormUrlEncodedContent(form), cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<TokenInfo>(json)
            ?? throw new InvalidOperationException("Empty token response from APS.");
    }
}
