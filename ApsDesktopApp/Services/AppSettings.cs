using System.IO;
using System.Text.Json;

namespace ApsDesktopApp.Services;

// Non-secret configuration. With PKCE there is no client secret to protect,
// so the Client ID is stored as plain JSON.
public class AppSettings
{
    public string ClientId { get; set; } = string.Empty;
    public int CallbackPort { get; set; } = 8080;

    // APS data-residency region for Data Management/OSS calls. We default to
    // "EMEA" because our accounts live in the European data center; Autodesk's
    // own API default is "US" (also valid: "APAC").
    public string Region { get; set; } = "EMEA";

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
