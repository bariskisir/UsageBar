using System.Text;
using UsageBar.Infrastructure.FileSystem;

namespace UsageBar.Infrastructure.Diagnostics;

internal static class FatalErrorLogger
{
    public static void Log(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(ApplicationPaths.AppDataDirectory);

            var message = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"))
                .AppendLine(" | Fatal startup failure.")
                .AppendLine(exception.ToString())
                .ToString();

            File.AppendAllText(ApplicationPaths.LogFilePath, message);
        }
        catch
        {
            // The app has no UI surface for startup failures; avoid cascading from logging.
        }
    }
}
