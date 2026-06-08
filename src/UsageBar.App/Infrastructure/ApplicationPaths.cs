namespace UsageBar.Infrastructure;

/// <summary>Well-known filesystem paths used by the application.</summary>
internal static class ApplicationPaths
{
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UsageBar");

    public static string SettingsFilePath { get; } = Path.Combine(AppDataDirectory, "settings.json");

    public static string LogFilePath { get; } = Path.Combine(AppDataDirectory, "app.log");

    public static string WebView2DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UsageBar",
        "WebView2");
}
