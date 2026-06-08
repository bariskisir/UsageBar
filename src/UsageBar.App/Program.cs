using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using UsageBar.Infrastructure;
using UsageBar.Tray;

namespace UsageBar;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Directory.CreateDirectory(ApplicationPaths.AppDataDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            // The built-in HttpClient logging emits an Information line per request; keep
            // app.log focused on application events.
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .WriteTo.File(
                ApplicationPaths.LogFilePath,
                rollingInterval: RollingInterval.Infinite,
                fileSizeLimitBytes: 5_000_000,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 5,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            using var provider = ServiceConfiguration.Build(Log.Logger);
            provider.GetRequiredService<TrayApplication>().Run();
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
