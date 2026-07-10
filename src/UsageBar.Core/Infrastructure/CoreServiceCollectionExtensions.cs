using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UsageBar.Core.Application;
using UsageBar.Core.Infrastructure;
using UsageBar.Core.Infrastructure.Logging;
using UsageBar.Core.Providers;

namespace UsageBar.Core.Infrastructure;

internal static class CoreServiceCollectionExtensions
{
    private const string UsageHttpClientName = "usage";

    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddTransient<UsageHttpTelemetryHandler>();

        services.AddHttpClient(UsageHttpClientName, client => client.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            })
            .AddHttpMessageHandler<UsageHttpTelemetryHandler>();
        services.AddSingleton(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient(UsageHttpClientName));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IProviderQueryContextFactory, SystemProviderQueryContextFactory>();
        services.AddSingleton(new UsageRefreshOptions(
            ForceAutomaticIconLayout: Environment.GetEnvironmentVariable("USAGEBAR_TEST") == "1"));
        services.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(
            PlatformPaths.SettingsFilePath,
            sp.GetRequiredService<ILogger<JsonSettingsStore>>()));

        services.AddSingleton<ICodexAuthReader, CodexAuthReader>();
        services.AddSingleton<IClaudeAuthReader, ClaudeAuthReader>();

        RegisterProviders(services);

        services.AddSingleton<ProviderInitializer>();
        services.AddSingleton<IUpdateService, UpdateService>();

        services.AddSingleton<IRemoteNotificationService, TelegramNotificationService>();
        services.AddSingleton<IRemoteNotificationService, DiscordNotificationService>();
        services.AddSingleton<IUsageRefreshService, UsageRefreshService>();

        return services;
    }

    private static void RegisterProviders(IServiceCollection services)
    {
        if (Environment.GetEnvironmentVariable("USAGEBAR_TEST") == "1")
        {
            services.AddSingleton<IUsageProvider, TestCodexProvider>();
            services.AddSingleton<IUsageProvider, TestClaudeProvider>();
            services.AddSingleton<IUsageProvider, TestElevenLabsProvider>();
            services.AddSingleton<IUsageProvider, TestKiloProvider>();
            services.AddSingleton<IUsageProvider, TestDeepSeekProvider>();
            services.AddSingleton<IUsageProvider, TestOpenRouterProvider>();
            services.AddSingleton<IUsageProvider, TestZenMuxProvider>();
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
            services.AddSingleton<IUsageProvider, ZenMuxProvider>();
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
    }
}
