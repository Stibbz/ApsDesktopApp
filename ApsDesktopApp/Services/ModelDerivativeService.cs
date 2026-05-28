using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ApsDesktopApp.Models;

namespace ApsDesktopApp.Services;

// Wraps the APS Model Derivative v2 API: start a translation job, poll its
// manifest, and download a converted output file. WPF-free.
// Unlike Data Management, this API IS region-routed -- every request carries
// the x-ads-region header sourced from AppSettings. Uses the handler-authed client.
public class ModelDerivativeService
{
    private const string Base = "https://developer.api.autodesk.com/modelderivative/v2/designdata";
    private const string Cat  = "ModelDerivative";

    private readonly HttpClient _http;
    private readonly AppLogger  _log;
    private AppSettings _settings;

    public ModelDerivativeService(HttpClient http, AppLogger log)
    {
        _http = http;
        _log  = log;
        _settings = AppSettings.Load();
    }

    public void ReloadSettings() => _settings = AppSettings.Load();

    // Submits a translation job for the given version URN.
    // outputFormat is the APS format token: "ifc", "dwg", "obj", "stl", "svf2", etc.
    // HTTP 409 means a job already exists for this URN/format -- treated as success.
    public async Task StartTranslationAsync(
        string versionUrn,
        string outputFormat,
        CancellationToken cancellationToken)
    {
        _log.Info(Cat, $"StartTranslation: format={outputFormat} urn={Short(versionUrn)}");

        var outputNode = outputFormat is "svf" or "svf2"
            ? new JsonObject { ["type"] = outputFormat, ["views"] = new JsonArray("2d", "3d") }
            : new JsonObject { ["type"] = outputFormat };

        var body = new JsonObject
        {
            ["input"] = new JsonObject { ["urn"] = ToBase64Url(versionUrn) },
            ["output"] = new JsonObject
            {
                ["formats"] = new JsonArray(outputNode)
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/job");
        request.Headers.Add("x-ads-region", RegionHeader());
        request.Headers.Add("x-ads-force", "true");
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            _log.Debug(Cat, "StartTranslation: HTTP 409 -- job already queued or complete");
            return;
        }
        await EnsureSuccessAsync(response, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        _log.Info(Cat, $"StartTranslation: job submitted (HTTP {(int)response.StatusCode})");
        _log.Debug(Cat, $"StartTranslation response: {(responseBody.Length > 500 ? responseBody[..500] : responseBody)}");
    }

    // Returns the manifest for the given URN, or null if no job has been started (404).
    public async Task<ManifestStatus?> GetManifestAsync(
        string versionUrn,
        CancellationToken cancellationToken)
    {
        _log.Debug(Cat, $"GetManifest: urn={Short(versionUrn)}");

        var urn = ToBase64Url(versionUrn);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Base}/{urn}/manifest");
        request.Headers.Add("x-ads-region", RegionHeader());

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _log.Debug(Cat, "GetManifest: HTTP 404 -- no manifest exists yet");
            return null;
        }
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var manifest = JsonSerializer.Deserialize<ManifestStatus>(json);
        _log.Debug(Cat, $"GetManifest: status={manifest?.Status ?? "null"} progress={manifest?.Progress ?? "?"}");

        if (manifest?.Derivatives is { Count: > 0 })
        {
            foreach (var d in manifest.Derivatives)
                _log.Debug(Cat, $"  derivative: outputType={d.OutputType} status={d.Status} children={d.Children?.Count ?? 0}");
        }
        else
        {
            _log.Debug(Cat, "  derivatives: (none)");
        }

        return manifest;
    }

    // Downloads a specific derivative resource (identified by its child URN from
    // the manifest) and returns the raw file bytes.
    public async Task<byte[]> DownloadDerivativeAsync(
        string versionUrn,
        string derivativeUrn,
        CancellationToken cancellationToken)
    {
        _log.Info(Cat, $"DownloadDerivative: {Short(derivativeUrn)}");

        var urn = ToBase64Url(versionUrn);
        var encodedDerivative = Uri.EscapeDataString(derivativeUrn);
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{Base}/{urn}/manifest/{encodedDerivative}");
        request.Headers.Add("x-ads-region", RegionHeader());

        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        _log.Info(Cat, $"DownloadDerivative: received {bytes.Length:N0} bytes");
        return bytes;
    }

    // APS accepts "US", "EMEA", or "APAC"; default to US if unset.
    private string RegionHeader() =>
        string.IsNullOrWhiteSpace(_settings.Region) ? "US" : _settings.Region;

    // Throws with the APS error body included so the status message is useful.
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        var trimmed = body.Length > 300 ? body[..300] : body;
        throw new HttpRequestException(
            $"{(int)response.StatusCode} {response.ReasonPhrase}: {trimmed}");
    }

    // URL-safe, unpadded Base64 of the URN, as Model Derivative requires.
    public static string ToBase64Url(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string Short(string s, int max = 50) =>
        s.Length <= max ? s : s[..max] + "...";
}
