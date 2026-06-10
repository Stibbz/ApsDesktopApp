using System;
using System.Linq;
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
// the "region" header sourced from AppSettings. Uses the handler-authed client.
public class ModelDerivativeService
{
    private const string Base = "https://developer.api.autodesk.com/modelderivative/v2/designdata";
    private const string LogCategory = "ModelDerivative";

    private readonly HttpClient _http;
    private readonly AppLogger  _log;

    public ModelDerivativeService(HttpClient http, AppLogger log)
    {
        _http = http;
        _log  = log;
    }

    // Submits a translation job for the given version URN.
    // outputFormat is the APS format token: "ifc", "dwg", "obj", "stl", "svf2", etc.
    // HTTP 409 means a job already exists for this URN/format -- treated as success.
    public async Task StartTranslationAsync(
        string versionUrn,
        string outputFormat,
        CancellationToken cancellationToken)
    {
        _log.Info(LogCategory, $"StartTranslation: format={outputFormat} urn={Short(versionUrn)}");

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
        request.Headers.Add("region", RegionHeader());
        request.Headers.Add("x-ads-force", "true");
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            _log.Debug(LogCategory, "StartTranslation: HTTP 409 -- job already queued or complete");
            return;
        }
        await EnsureSuccessAsync(response, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        _log.Info(LogCategory, $"StartTranslation: job submitted (HTTP {(int)response.StatusCode})");
        _log.Debug(LogCategory, $"StartTranslation response: {(responseBody.Length > 500 ? responseBody[..500] : responseBody)}");
    }

    // Returns the manifest for the given URN, or null if no job has been started (404).
    public async Task<ManifestStatus?> GetManifestAsync(
        string versionUrn,
        CancellationToken cancellationToken)
    {
        _log.Debug(LogCategory, $"GetManifest: urn={Short(versionUrn)}");

        var urn = ToBase64Url(versionUrn);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Base}/{urn}/manifest");
        request.Headers.Add("region", RegionHeader());

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _log.Debug(LogCategory, "GetManifest: HTTP 404 -- no manifest exists yet");
            return null;
        }
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var manifest = JsonSerializer.Deserialize<ManifestStatus>(json);
        _log.Debug(LogCategory, $"GetManifest: status={manifest?.Status ?? "null"} progress={manifest?.Progress ?? "?"}");

        if (manifest?.Derivatives is { Count: > 0 })
        {
            foreach (var d in manifest.Derivatives)
                _log.Debug(LogCategory, $"  derivative: outputType={d.OutputType} status={d.Status} children={d.Children?.Count ?? 0}");
        }
        else
        {
            _log.Debug(LogCategory, "  derivatives: (none)");
        }

        return manifest;
    }

    // Downloads a specific derivative resource (identified by its child URN from
    // the manifest) and returns the raw file bytes. Two-step flow: the direct
    // GET .../manifest/{derivativeUrn} download was decommissioned, so first
    // fetch signed CloudFront cookies, then GET the returned URL with them.
    public async Task<byte[]> DownloadDerivativeAsync(
        string versionUrn,
        string derivativeUrn,
        CancellationToken cancellationToken)
    {
        _log.Info(LogCategory, $"DownloadDerivative: {Short(derivativeUrn)}");

        var urn = ToBase64Url(versionUrn);
        var encodedDerivative = Uri.EscapeDataString(derivativeUrn);
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{Base}/{urn}/manifest/{encodedDerivative}/signedcookies");
        request.Headers.Add("region", RegionHeader());

        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var url = JsonNode.Parse(json)?["url"]?.GetValue<string>();
        if (string.IsNullOrEmpty(url))
            throw new HttpRequestException("Signed-cookies response did not contain a download URL.");
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            throw new HttpRequestException("Signed-cookies response did not contain cookies.");

        // The CloudFront host authenticates via the signed cookies, not the
        // bearer token, so the second GET bypasses the authed client. Only the
        // name=value part of each Set-Cookie is forwarded.
        var cookieHeader = string.Join("; ", setCookies.Select(c => c.Split(';')[0]));
        using var download = new HttpRequestMessage(HttpMethod.Get, url);
        download.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

        using var downloadResponse = await DownloadClient.SendAsync(download, cancellationToken);
        await EnsureSuccessAsync(downloadResponse, cancellationToken);
        var bytes = await downloadResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        _log.Info(LogCategory, $"DownloadDerivative: received {bytes.Length:N0} bytes");
        return bytes;
    }

    // UseCookies=false is required: with the default cookie container enabled,
    // HttpClientHandler silently drops a manually set Cookie header.
    private static readonly HttpClient DownloadClient =
        new(new HttpClientHandler { UseCookies = false });

    // Valid regions per the SDK: US (default), EMEA, AUS, CAN, DEU, IND, JPN, GBR.
    // "APAC" was renamed "AUS" -- mapped here so older settings.json files keep working.
    // Loaded fresh each call so region changes in Settings take effect without restart.
    private static string RegionHeader()
    {
        var region = AppSettings.Load().Region;
        if (string.IsNullOrWhiteSpace(region)) return "US";
        return region.Equals("APAC", StringComparison.OrdinalIgnoreCase) ? "AUS" : region;
    }

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
