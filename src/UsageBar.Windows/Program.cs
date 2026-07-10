using Microsoft.Extensions.DependencyInjection;
using Serilog;
using UsageBar.Core.Infrastructure;
using UsageBar.Core.Infrastructure.Logging;
using UsageBar.Windows.Tray;

namespace UsageBar.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Directory.CreateDirectory(PlatformPaths.AppDataDirectory);

        SerilogBootstrap.CreateLogger(PlatformPaths.LogFilePath);

        try
        {
            Log.Information(
                "Usage Bar starting. Version={Version}; OS={OS}; Architecture={Architecture}; MinimumLogLevel={MinimumLogLevel}; HttpBodyLogging={HttpBodyLogging}.",
                typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
                Environment.OSVersion.VersionString,
                System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
                LogConfiguration.MinimumLevel,
                LogConfiguration.IsHttpBodyLoggingEnabled);
            using var provider = ServiceConfiguration.Build(Log.Logger);
            provider.GetRequiredService<TrayApplication>().Run();
            Log.Information("Usage Bar stopped normally.");
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Fatal startup failure.");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
