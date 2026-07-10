using Serilog;
using Serilog.Events;

namespace UsageBar.Core.Infrastructure.Logging;

public static class SerilogBootstrap
{
    public static Serilog.ILogger CreateLogger(string logFilePath)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(LogConfiguration.MinimumLevel)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Infinite,
                fileSizeLimitBytes: 5_000_000,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 5,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}")
            .CreateLogger();

        return Log.Logger;
    }
}
