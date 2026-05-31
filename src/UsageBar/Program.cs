using UsageBar.Application;
using UsageBar.Infrastructure.Diagnostics;

namespace UsageBar;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            using var host = UsageBarHost.CreateDefault();
            host.Run();
        }
        catch (Exception exception)
        {
            FatalErrorLogger.Log(exception);
        }
    }
}
