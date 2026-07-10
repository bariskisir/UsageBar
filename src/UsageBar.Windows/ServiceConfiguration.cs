using Microsoft.Extensions.DependencyInjection;
using Serilog;
using UsageBar.Core.Application;
using UsageBar.Core.Infrastructure;
using UsageBar.Core.Providers;
using UsageBar.Windows.Infrastructure;
using UsageBar.Windows.Settings;
using UsageBar.Windows.Tooltip;
using UsageBar.Windows.Tray;

namespace UsageBar.Windows;

internal static class ServiceConfiguration
{
    public static ServiceProvider Build(Serilog.ILogger serilogLogger)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddSerilog(serilogLogger, dispose: false));
        services.AddCoreServices();

        services.AddSingleton<IAntigravityAuthReader, AntigravityAuthReader>();

        services.AddSingleton<WebViewEnvironment>();
        services.AddSingleton<IStartupRegistrationService, StartupRegistrationService>();

        services.AddSingleton<ITrayContextMenu, TrayContextMenu>();
        services.AddSingleton<ITrayIconWindow, TrayIconWindow>();
        services.AddSingleton<IWebViewTooltip, WebViewTooltip>();
        services.AddSingleton<SettingsPanel>();
        services.AddSingleton<IUsageView, TrayUsageView>();
        services.AddSingleton<TrayApplication>();

        return services.BuildServiceProvider();
    }
}
