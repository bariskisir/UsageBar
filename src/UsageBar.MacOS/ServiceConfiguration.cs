using Microsoft.Extensions.DependencyInjection;
using Serilog;
using UsageBar.Core.Application;
using UsageBar.Core.Infrastructure;
using UsageBar.Core.Providers;
using UsageBar.MacOS.Infrastructure;
using UsageBar.MacOS.Settings;
using UsageBar.MacOS.Tooltip;
using UsageBar.MacOS.Tray;

namespace UsageBar.MacOS;

internal static class ServiceConfiguration
{
    public static ServiceProvider Build(Serilog.ILogger serilogLogger)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddSerilog(serilogLogger, dispose: false));
        services.AddCoreServices();

        services.AddSingleton<IAntigravityAuthReader, AntigravityAuthReader>();
        services.AddSingleton<IStartupRegistrationService, StartupRegistrationService>();

        services.AddSingleton<NativeTray>();
        services.AddSingleton<NativeTooltip>();
        services.AddSingleton<SettingsPanel>();
        services.AddSingleton<IUsageView, UsageView>();
        services.AddSingleton<TrayApplication>();

        return services.BuildServiceProvider();
    }
}