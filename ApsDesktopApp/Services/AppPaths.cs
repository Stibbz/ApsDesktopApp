using System;
using System.IO;

namespace ApsDesktopApp.Services;

// Centralizes the %APPDATA%\ApsDesktopApp location used for settings and tokens.
public static class AppPaths
{
    public static string DataDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ApsDesktopApp");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string TokensFile => Path.Combine(DataDirectory, "tokens.dat");
    public static string MdSecretFile => Path.Combine(DataDirectory, "md_secret.dat");
}
