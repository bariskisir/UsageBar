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

        services.AddHttpClient(UsageHttpClientName, client => client.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Recycle connections every 10 minutes so DNS changes are picked up even
                // though the HttpClient itself is held as a singleton for the app lifetime.
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            });
        services.AddSingleton(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient(UsageHttpClientName));

        // Configuration + cross-cutting services.
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(
            ApplicationPaths.SettingsFilePath,
            sp.GetRequiredService<ILogger<JsonSettingsStore>>()));

        // Metric-provider auth readers.
        services.AddSingleton<ICodexAuthReader, CodexAuthReader>();
        services.AddSingleton<IClaudeAuthReader, ClaudeAuthReader>();
        services.AddSingleton<IAntigravityAuthReader, AntigravityAuthReader>();

        // Providers — add a new one here (and its folder under Providers/) to extend the app.
        // When USAGEBAR_TEST=1, test providers are used instead so every provider returns
        // random mock data on every refresh without real API keys or auth files.
        if (Environment.GetEnvironmentVariable("USAGEBAR_TEST") == "1")
        {
            services.AddSingleton<IUsageProvider, TestCodexProvider>();
            services.AddSingleton<IUsageProvider, TestClaudeProvider>();
            services.AddSingleton<IUsageProvider, TestElevenLabsProvider>();
            services.AddSingleton<IUsageProvider, TestKiloProvider>();
            services.AddSingleton<IUsageProvider, TestDeepSeekProvider>();
            services.AddSingleton<IUsageProvider, TestOpenRouterProvider>();
            services.AddSingleton<IUsageProvider, TestMoonshotProvider>();
            services.AddSingleton<IUsageProvider, TestDeepgramProvider>();
            services.AddSingleton<IUsageProvider, TestOpenAIProvider>();
            services.AddSingleton<IUsageProvider, TestVeniceProvider>();
            services.AddSingleton<IUsageProvider, TestCopilotProvider>();
            services.AddSingleton<IUsageProvider, TestCrofProvider>();
            services.AddSingleton<IUsageProvider, TestCodebuffProvider>();
            services.AddSingleton<IUsageProvider, TestWarpProvider>();
            services.AddSingleton<IUsageProvider, TestZaiProvider>();
            services.AddSingleton<IUsageProvider, TestSyntheticProvider>();
            services.AddSingleton<IUsageProvider, TestChutesProvider>();
            services.AddSingleton<IUsageProvider, TestMiniMaxProvider>();
            services.AddSingleton<IUsageProvider, TestPoeProvider>();
            services.AddSingleton<IUsageProvider, TestAlibabaProvider>();
            services.AddSingleton<IUsageProvider, TestAntigravityProvider>();
        }
        else
        {
            services.AddSingleton<IUsageProvider, CodexProvider>();
            services.AddSingleton<IUsageProvider, ClaudeProvider>();
            services.AddSingleton<IUsageProvider, ElevenLabsProvider>();
            services.AddSingleton<IUsageProvider, KiloProvider>();
            services.AddSingleton<IUsageProvider, DeepSeekProvider>();
            services.AddSingleton<IUsageProvider, OpenRouterProvider>();
            services.AddSingleton<IUsageProvider, MoonshotProvider>();
            services.AddSingleton<IUsageProvider, DeepgramProvider>();
            services.AddSingleton<IUsageProvider, OpenAIProvider>();
            services.AddSingleton<IUsageProvider, VeniceProvider>();
            services.AddSingleton<IUsageProvider, CopilotProvider>();
            services.AddSingleton<IUsageProvider, CrofProvider>();
            services.AddSingleton<IUsageProvider, CodebuffProvider>();
            services.AddSingleton<IUsageProvider, WarpProvider>();
            services.AddSingleton<IUsageProvider, ZaiProvider>();
            services.AddSingleton<IUsageProvider, SyntheticProvider>();
            services.AddSingleton<IUsageProvider, ChutesProvider>();
            services.AddSingleton<IUsageProvider, MiniMaxProvider>();
            services.AddSingleton<IUsageProvider, PoeProvider>();
            services.AddSingleton<IUsageProvider, AlibabaProvider>();
            services.AddSingleton<IUsageProvider, AntigravityProvider>();
        }

        // Windows shell.
        services.AddSingleton<IStartupRegistrationService, StartupRegistrationService>();
        services.AddSingleton<ITrayContextMenu, TrayContextMenu>();
        services.AddSingleton<ITrayIconWindow, TrayIconWindow>();
        services.AddSingleton<IWebViewTooltip, WebViewTooltip>();
        services.AddSingleton<IUsageView, TrayUsageView>();
        services.AddSingleton<IUpdateService, UpdateService>();

        // Orchestration + application root.
        services.AddSingleton<IRemoteNotificationService, TelegramNotificationService>();
        services.AddSingleton<IRemoteNotificationService, DiscordNotificationService>();
        services.AddSingleton<IUsageRefreshService, UsageRefreshService>();
        services.AddSingleton<TrayApplication>();

        return services.BuildServiceProvider();
    }
}
