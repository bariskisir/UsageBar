using System.Text;

namespace UsageBar.Infrastructure.Diagnostics;

internal sealed class AppLogger : IDisposable
{
    private readonly string _logFilePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppLogger(string logFilePath)
    {
        _logFilePath = logFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath)!);
    }

    public async Task LogAsync(string message, Exception? exception = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            var builder = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"))
                .Append(" | ")
                .Append(message);

            if (exception is not null)
            {
                builder
                    .AppendLine()
                    .Append(exception);
            }

            builder.AppendLine();
            await File.AppendAllTextAsync(_logFilePath, builder.ToString()).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}
