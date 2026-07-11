namespace UsageBar.Core.Infrastructure;

/// <summary>Portable per-user paths used by every desktop host.</summary>
public static class PlatformPaths
{
    public static string AppDataDirectory { get; } = Path.Combine(GetApplicationDataRoot(), "UsageBar");

    public static string SettingsFilePath { get; } = Path.Combine(AppDataDirectory, "settings.json");

    public static string LogFilePath { get; } = Path.Combine(AppDataDirectory, "app.log");

    public static string AntigravityCredentialsFilePath { get; } = Path.Combine(
        AppDataDirectory,
        "antigravity",
        "oauth_creds.json");

    private static string GetApplicationDataRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "Application Support");
        }

        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return string.IsNullOrWhiteSpace(xdgConfigHome)
            ? Path.Combine(home, ".config")
            : xdgConfigHome;
    }
}