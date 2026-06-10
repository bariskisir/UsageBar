using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using UsageBar.Application;
using UsageBar.Infrastructure;
using UsageBar.Providers;
using UsageBar.Tooltip;
using UsageBar.Tray;

namespace UsageBar;

/// <summary>
/// Builds the dependency-injection container. Everything the app depends on is registered
/// behind an interface where it makes sense (settings, clock, view, providers, auth readers),
/// so the platform-agnostic core has no compile-time knowledge of the Windows shell.
/// </summary>
internal static class ServiceConfiguration
{
    private const string UsageHttpClientName = "usage";

    public static ServiceProvider Build(Serilog.ILogger serilogLogger)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddSerilog(serilogLogger, dispose: false));

        services.AddHttpClient(UsageHttpClientName, client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddSingleton(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient(UsageHttpClientName));

        // Configuration + cross-cutting services.
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(
            ApplicationPaths.SettingsFilePath,
            sp.GetRequiredService<ILogger<JsonSettingsStore>>()));

        // Metric-provider auth readers.
        services.AddSingleton<ICodexAuthReader, CodexAuthReader>();
        services.AddSingleton<IClaudeAuthReader, ClaudeAuthReader>();

        // Providers — add a new one here (and its folder under Providers/) to extend the app.
        services.AddSingleton<IUsageProvider, CodexProvider>();
        services.AddSingleton<IUsageProvider, ClaudeProvider>();
        services.AddSingleton<IUsageProvider, DeepSeekProvider>();
        services.AddSingleton<IUsageProvider, OpenRouterProvider>();
        services.AddSingleton<IUsageProvider, DeepgramProvider>();

        // Windows shell.
        services.AddSingleton<IStartupRegistrationService, StartupRegistrationService>();
        services.AddSingleton<ITrayContextMenu, TrayContextMenu>();
        services.AddSingleton<ITrayIconWindow, TrayIconWindow>();
        services.AddSingleton<IWebViewTooltip, WebViewTooltip>();
        services.AddSingleton<IUsageView, TrayUsageView>();

        // Orchestration + application root.
        services.AddSingleton<IRemoteNotificationService, TelegramNotificationService>();
        services.AddSingleton<IUsageRefreshService, UsageRefreshService>();
        services.AddSingleton<TrayApplication>();

        return services.BuildServiceProvider();
    }
}
