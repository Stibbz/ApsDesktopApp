using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ApsDesktopApp.Models;

namespace ApsDesktopApp.Services;

// Model Derivative: trigger a translation job and poll its manifest. WPF-free.
// Unlike Data Management, this API IS region-routed -- every request carries the
// x-ads-region header from AppSettings.Region. Uses the handler-authed client.
public class ModelDerivativeService
{
    private const string Base = "https://developer.api.autodesk.com/modelderivative/v2/designdata";

    private readonly HttpClient _http;
    private AppSettings _settings;

    public ModelDerivativeService(HttpClient http)
    {
        _http = http;
        _settings = AppSettings.Load();
    }

    public void ReloadSettings() => _settings = AppSettings.Load();

    // Starts an SVF2 translation for the given version URN (raw, not yet
    // encoded). Returns once APS has accepted the job; poll the manifest for
    // progress.
    public async Task StartTranslationAsync(string versionUrn, CancellationToken cancellationToken)
    {
        var body = new
        {
            input = new { urn = ToBase64Url(versionUrn) },
            output = new
            {
                formats = new[]
                {
                    new { type = "svf2", views = new[] { "2d", "3d" } }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/job");
        request.Headers.Add("x-ads-region", RegionHeader());
        request.Content = new StringContent(
            JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    // Fetches the translation manifest. Returns null if the URN has no manifest
    // yet (HTTP 404), i.e. no job has been started for it.
    public async Task<ManifestStatus?> GetManifestAsync(string versionUrn, CancellationToken cancellationToken)
    {
        var urn = ToBase64Url(versionUrn);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Base}/{urn}/manifest");
        request.Headers.Add("x-ads-region", RegionHeader());

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<ManifestStatus>(json);
    }

    // APS accepts "US", "EMEA", or "APAC"; default to US if unset.
    private string RegionHeader() =>
        string.IsNullOrWhiteSpace(_settings.Region) ? "US" : _settings.Region;

    // URL-safe, unpadded Base64 of the URN, as Model Derivative requires.
    private static string ToBase64Url(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
