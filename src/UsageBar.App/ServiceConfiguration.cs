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

        // Configuration + cross-cutting services.
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(
            ApplicationPaths.SettingsFilePath,
            sp.GetRequiredService<ILogger<JsonSettingsStore>>()));

        // Metric-provider auth readers.
        services.AddSingleton<ICodexAuthReader, CodexAuthReader>();
        services.AddSingleton<IClaudeAuthReader, ClaudeAuthReader>();

        // Providers — add a new one here (and its folder under Providers/) to extend the app.
        services.AddSingleton<IUsageProvider>(sp => new CodexProvider(HttpClient(sp), sp.GetRequiredService<ICodexAuthReader>()));
        services.AddSingleton<IUsageProvider>(sp => new ClaudeProvider(HttpClient(sp), sp.GetRequiredService<IClaudeAuthReader>()));
        services.AddSingleton<IUsageProvider>(sp => new DeepSeekProvider(HttpClient(sp)));
        services.AddSingleton<IUsageProvider>(sp => new OpenRouterProvider(HttpClient(sp)));
        services.AddSingleton<IUsageProvider>(sp => new DeepgramProvider(HttpClient(sp)));

        // Windows shell.
        services.AddSingleton<StartupRegistrationService>();
        services.AddSingleton<TrayContextMenu>();
        services.AddSingleton<TrayIconWindow>();
        services.AddSingleton<WebViewTooltip>();
        services.AddSingleton<IUsageView, TrayUsageView>();

        // Orchestration + application root.
        services.AddSingleton<TelegramNotifier>(sp => new TelegramNotifier(HttpClient(sp), sp.GetRequiredService<ILogger<TelegramNotifier>>()));
        services.AddSingleton<UsageRefreshService>();
        services.AddSingleton<TrayApplication>();

        return services.BuildServiceProvider();
    }

    private static HttpClient HttpClient(IServiceProvider services) =>
        services.GetRequiredService<IHttpClientFactory>().CreateClient(UsageHttpClientName);
}
