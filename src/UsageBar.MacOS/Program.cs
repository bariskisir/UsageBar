using AppKit;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using UsageBar.Core.Infrastructure;
using UsageBar.Core.Infrastructure.Logging;
using UsageBar.MacOS.Tray;

namespace UsageBar.MacOS;
internal static class Program
{
    private static void Main(string[] args)
    {
        NSApplication.Init();
        Directory.CreateDirectory(PlatformPaths.AppDataDirectory);
        SerilogBootstrap.CreateLogger(PlatformPaths.LogFilePath);
        using (var provider = ServiceConfiguration.Build(Log.Logger))
        {
            provider.GetRequiredService<TrayApplication>().Run();
        }
    }
}