using Serilog.Events;

namespace UsageBar.Core.Infrastructure.Logging;

internal static class LogConfiguration
{
    private const string LogLevelVariable = "USAGEBAR_LOG_LEVEL";
    private const string HttpBodyVariable = "USAGEBAR_HTTP_BODY_LOGGING";

    public static LogEventLevel MinimumLevel =>
        Environment.GetEnvironmentVariable(LogLevelVariable)?.Trim().ToLowerInvariant() switch
        {
            "trace" or "verbose" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "warning" or "warn" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            _ => LogEventLevel.Information,
        };

    public static bool IsHttpBodyLoggingEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(HttpBodyVariable), "1", StringComparison.Ordinal) ||
        string.Equals(Environment.GetEnvironmentVariable(HttpBodyVariable), "true", StringComparison.OrdinalIgnoreCase);
}