using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UsageBar.Core.Application;
using UsageBar.Core.Infrastructure;
using UsageBar.Core.Infrastructure.Logging;
using UsageBar.Core.Providers;
using UsageBar.Core.Settings;

namespace UsageBar.Core.Infrastructure;

internal static class CoreServiceCollectionExtensions
{
    private const string UsageHttpClientName = "usage";

    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddTransient<UsageHttpTelemetryHandler>();

        services.AddHttpClient(UsageHttpClientName, client => client.Timeout = TimeSpan.FromSeconds(20))
            // The factory's default logger adds the raw request URI to an outer scope. That
            // leaks path credentials such as Telegram bot tokens even when our telemetry
            // handler logs a redacted URI.
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            })
            .AddHttpMessageHandler<UsageHttpTelemetryHandler>();
        services.AddSingleton(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient(UsageHttpClientName));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IProviderQueryContextFactory, SystemProviderQueryContextFactory>();
        services.AddSingleton(new UsageRefreshOptions(
            ForceAutomaticIconLayout: Environment.GetEnvironmentVariable("USAGEBAR_TEST") == "1",
            ProviderTimeout: TimeSpan.FromSeconds(45)));
        services.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(
            PlatformPaths.SettingsFilePath,
            sp.GetRequiredService<ILogger<JsonSettingsStore>>()));

        services.AddSingleton<ICodexAuthReader, CodexAuthReader>();
        services.AddSingleton<IClaudeAuthReader, ClaudeAuthReader>();

        RegisterProviders(services);

        services.AddSingleton<ProviderInitializer>();
        services.AddSingleton<IUsageAggregator, UsageAggregator>();
        services.AddSingleton<IWindowStartRequestSender, WindowStartRequestSender>();
        services.AddSingleton<IUsageWindowStartService, UsageWindowStartService>();
        services.AddSingleton<IThresholdNotificationDispatcher>(sp => new ThresholdNotificationDispatcher(
            sp.GetRequiredService<IUsageView>(),
            sp.GetServices<IRemoteNotificationService>(),
            sp.GetRequiredService<ILogger<ThresholdNotificationDispatcher>>()));
        services.AddSingleton<IRefreshCycleRunner, RefreshCycleRunner>();
        services.AddSingleton<DesktopApplicationCoordinator>();
        services.AddSingleton<SettingsController>();
        services.AddSingleton<IUpdateService, UpdateService>();

        services.AddSingleton<IRemoteNotificationService, TelegramNotificationService>();
        services.AddSingleton<IRemoteNotificationService, DiscordNotificationService>();
        services.AddSingleton<IUsageRefreshService, UsageRefreshService>();

        return services;
    }

    private static void RegisterProviders(IServiceCollection services)
    {
        var useDemoProviders = Environment.GetEnvironmentVariable("USAGEBAR_TEST") == "1";
        RegisterProvider<CodexProvider, TestCodexProvider>(services, useDemoProviders);
        RegisterProvider<ClaudeProvider, TestClaudeProvider>(services, useDemoProviders);
        RegisterProvider<ElevenLabsProvider, TestElevenLabsProvider>(services, useDemoProviders);
        RegisterProvider<KiloProvider, TestKiloProvider>(services, useDemoProviders);
        RegisterProvider<DeepSeekProvider, TestDeepSeekProvider>(services, useDemoProviders);
        RegisterProvider<OpenRouterProvider, TestOpenRouterProvider>(services, useDemoProviders);
        RegisterProvider<ZenMuxProvider, TestZenMuxProvider>(services, useDemoProviders);
        RegisterProvider<MoonshotProvider, TestMoonshotProvider>(services, useDemoProviders);
        RegisterProvider<DeepgramProvider, TestDeepgramProvider>(services, useDemoProviders);
        RegisterProvider<OpenAIProvider, TestOpenAIProvider>(services, useDemoProviders);
        RegisterProvider<VeniceProvider, TestVeniceProvider>(services, useDemoProviders);
        RegisterProvider<CopilotProvider, TestCopilotProvider>(services, useDemoProviders);
        RegisterProvider<CrofProvider, TestCrofProvider>(services, useDemoProviders);
        RegisterProvider<CodebuffProvider, TestCodebuffProvider>(services, useDemoProviders);
        RegisterProvider<WarpProvider, TestWarpProvider>(services, useDemoProviders);
        RegisterProvider<ZaiProvider, TestZaiProvider>(services, useDemoProviders);
        RegisterProvider<SyntheticProvider, TestSyntheticProvider>(services, useDemoProviders);
        RegisterProvider<ChutesProvider, TestChutesProvider>(services, useDemoProviders);
        RegisterProvider<MiniMaxProvider, TestMiniMaxProvider>(services, useDemoProviders);
        RegisterProvider<PoeProvider, TestPoeProvider>(services, useDemoProviders);
        RegisterProvider<AlibabaProvider, TestAlibabaProvider>(services, useDemoProviders);
        RegisterProvider<AntigravityProvider, TestAntigravityProvider>(services, useDemoProviders);
    }

    private static void RegisterProvider<TProvider, TDemoProvider>(
        IServiceCollection services,
        bool useDemoProvider)
        where TProvider : class, IUsageProvider
        where TDemoProvider : class, IUsageProvider =>
        services.AddSingleton(
            typeof(IUsageProvider),
            useDemoProvider ? typeof(TDemoProvider) : typeof(TProvider));
}
