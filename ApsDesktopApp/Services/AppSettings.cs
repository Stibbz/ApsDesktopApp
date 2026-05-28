using System.IO;
using System.Text.Json;

namespace ApsDesktopApp.Services;

// Non-secret configuration stored as plain JSON. Secrets (e.g. the Model
// Derivative client secret) are kept separately in SecretStorage (DPAPI).
public class AppSettings
{
    public string ClientId { get; set; } = string.Empty;
    public int CallbackPort { get; set; } = 8080;

    // Client ID of the separate "Server-side Web App" APS app used for
    // 2-legged Model Derivative calls. Its client secret lives in SecretStorage.
    public string ModelDerivativeClientId { get; set; } = string.Empty;

    // APS data-residency region for Data Management/OSS calls. We default to
    // "EMEA" because our accounts live in the European data center; Autodesk's
    // own API default is "US" (also valid: "APAC").
    public string Region { get; set; } = "EMEA";

    // Last project selected in the unified project picker - restored on next session.
    public string LastProjectId { get; set; } = string.Empty;

    public string RedirectUri => $"http://localhost:{CallbackPort}/callback";

    public static AppSettings Load()
    {
        var path = AppPaths.SettingsFile;
        if (!File.Exists(path))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AppPaths.SettingsFile, json);
    }
}
