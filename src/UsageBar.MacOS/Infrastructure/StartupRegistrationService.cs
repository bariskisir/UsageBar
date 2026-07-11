using System.Diagnostics;
using UsageBar.Core.Infrastructure;

namespace UsageBar.MacOS.Infrastructure;
internal sealed class StartupRegistrationService : IStartupRegistrationService
{
    private static readonly string LaunchAgentsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");
    private static readonly string PlistPath = Path.Combine(LaunchAgentsDir, "com.usagebar.plist");
    private static readonly string? ExePath;
    static StartupRegistrationService()
    {
        using (var process = Process.GetCurrentProcess())
        {
            ExePath = process.MainModule?.FileName;
        }
    }

    public void Register()
    {
        if (string.IsNullOrWhiteSpace(ExePath))
        {
            return;
        }

        Directory.CreateDirectory(LaunchAgentsDir);
        var plist = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>Label</key>
                <string>com.usagebar</string>
                <key>ProgramArguments</key>
                <array>
                    <string>{ExePath}</string>
                </array>
                <key>RunAtLoad</key>
                <true/>
                <key>KeepAlive</key>
                <false/>
            </dict>
            </plist>
            """;
        File.WriteAllText(PlistPath, plist);
    }

    public void Unregister()
    {
        try
        {
            if (File.Exists(PlistPath))
            {
                File.Delete(PlistPath);
            }
        }
        catch
        {
        }
    }
}