using Serilog;
using UsageBar.Core.Infrastructure.Logging;
using Xunit;

namespace UsageBar.Tests;

public sealed class SerilogBootstrapTests
{
    [Fact]
    public void Creates_missing_logs_directory_and_uses_daily_date_filename()
    {
        var root = Path.Combine(Path.GetTempPath(), $"usagebar-log-test-{Guid.NewGuid():N}");
        var logPath = Path.Combine(root, "logs", ".log");
        try
        {
            var logger = SerilogBootstrap.CreateLogger(logPath);
            logger.Information("test entry");
            Log.CloseAndFlush();

            Assert.True(Directory.Exists(Path.Combine(root, "logs")));
            Assert.True(File.Exists(Path.Combine(root, "logs", $"{DateTimeOffset.Now:yyyyMMdd}.log")));
        }
        finally
        {
            Log.CloseAndFlush();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
