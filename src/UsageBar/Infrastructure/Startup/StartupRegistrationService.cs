using Microsoft.Win32;
using UsageBar.Infrastructure.Diagnostics;

namespace UsageBar.Infrastructure.Startup;

internal static class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "UsageBar";

    public static void EnsureRegistered(AppLogger logger)
    {
        try
        {
            var executablePath = GetExecutablePath();
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException("Could not resolve the UsageBar executable path.");
            }

            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (runKey is null)
            {
                throw new InvalidOperationException("Could not open the current-user startup registry key.");
            }

            var currentCommand = runKey.GetValue(ValueName) as string;
            if (PointsToExecutable(currentCommand, executablePath))
            {
                return;
            }

            runKey.SetValue(ValueName, Quote(executablePath), RegistryValueKind.String);
        }
        catch (Exception exception)
        {
            logger.LogAsync("Failed to register UsageBar as a startup app.", exception).GetAwaiter().GetResult();
        }
    }

    private static string? GetExecutablePath()
    {
        if (IsUsageBarExecutable(Environment.ProcessPath))
        {
            return Environment.ProcessPath;
        }

        var appHostPath = Path.Combine(AppContext.BaseDirectory, "UsageBar.exe");
        return File.Exists(appHostPath) ? appHostPath : null;
    }

    private static bool IsUsageBarExecutable(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
            string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetFileName(path), "dotnet.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool PointsToExecutable(string? command, string executablePath)
    {
        var currentPath = ExtractExecutablePath(command);
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(currentPath),
            Path.GetFullPath(executablePath),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractExecutablePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var closingQuoteIndex = trimmed.IndexOf('"', 1);
            return closingQuoteIndex > 1 ? trimmed[1..closingQuoteIndex] : null;
        }

        var executableMarkerIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (executableMarkerIndex >= 0)
        {
            return trimmed[..(executableMarkerIndex + ".exe".Length)];
        }

        return trimmed;
    }

    private static string Quote(string path)
    {
        return $"\"{path}\"";
    }
}
