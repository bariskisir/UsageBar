using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Diagnostics;
using UsageBar.Core.Infrastructure;
using UsageBar.Core.Infrastructure.Logging;

namespace UsageBar.Linux;
internal static class Program
{
    private static void Main(string[] args)
    {
        if (RequiresXWaylandRestart())
        {
            Environment.ExitCode = RestartWithXWayland(args);
            return;
        }

        Gtk.Application.Init();
        Directory.CreateDirectory(PlatformPaths.AppDataDirectory);
        SerilogBootstrap.CreateLogger(PlatformPaths.LogFilePath);
        Log.Information(
            "GTK display initialised: display={DisplayName}; backend={Backend}.",
            Gdk.Display.Default?.Name ?? "unknown",
            Environment.GetEnvironmentVariable("GDK_BACKEND") ?? "default");
        using (var provider = ServiceConfiguration.Build(Log.Logger))
        {
            provider.GetRequiredService<TrayApplication>().Run();
        }
    }

    private static bool RequiresXWaylandRestart() =>
        string.Equals(
            Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
            "wayland",
            StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"))
        && !string.Equals(
            Environment.GetEnvironmentVariable("GDK_BACKEND"),
            "x11",
            StringComparison.OrdinalIgnoreCase);

    private static int RestartWithXWayland(string[] args)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not locate the UsageBar executable.");
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
        };

        if (string.Equals(
                Path.GetFileNameWithoutExtension(executable),
                "dotnet",
                StringComparison.OrdinalIgnoreCase))
        {
            var entryAssembly = Environment.GetCommandLineArgs().FirstOrDefault()
                ?? throw new InvalidOperationException("Could not locate the UsageBar assembly.");
            startInfo.ArgumentList.Add(entryAssembly);
        }

        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GDK_BACKEND"] = "x11";
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not restart UsageBar with XWayland.");
        process.WaitForExit();
        return process.ExitCode;
    }
}
