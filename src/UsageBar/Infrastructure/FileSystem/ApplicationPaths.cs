namespace UsageBar.Infrastructure.FileSystem;

internal static class ApplicationPaths
{
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UsageBar");

    public static string SettingsFilePath { get; } = Path.Combine(AppDataDirectory, "settings.json");

    public static string LogFilePath { get; } = Path.Combine(AppDataDirectory, "app.log");
}
