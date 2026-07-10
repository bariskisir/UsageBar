using System.Diagnostics;
using UsageBar.Core.Infrastructure;

namespace UsageBar.Linux.Infrastructure;

internal sealed class StartupRegistrationService : IStartupRegistrationService
{
    private static readonly string AutostartDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config",
        "autostart");

    private static readonly string DesktopFilePath = Path.Combine(
        AutostartDir,
        "usagebar.desktop");

    private static readonly string? ExePath;

    static StartupRegistrationService()
    {
        using var process = Process.GetCurrentProcess();
        ExePath = process.MainModule?.FileName;
    }

    public void Register()
    {
        if (string.IsNullOrWhiteSpace(ExePath))
        {
            return;
        }

        Directory.CreateDirectory(AutostartDir);

        var desktopFile = $"""
            [Desktop Entry]
            Type=Application
            Name=Usage Bar
            Comment=LLM/API usage monitor
            Exec={ExePath}
            Terminal=false
            Categories=Utility;
            X-GNOME-Autostart-enabled=true
            """;

        File.WriteAllText(DesktopFilePath, desktopFile);
    }

    public void Unregister()
    {
        try
        {
            if (File.Exists(DesktopFilePath))
            {
                File.Delete(DesktopFilePath);
            }
        }
        catch
        {
        }
    }
}
