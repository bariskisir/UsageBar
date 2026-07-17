using Microsoft.Extensions.Logging;
using System.Reflection;
using UsageBar.Core.Application;
using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;
using UsageBar.Core.Infrastructure;
using UsageBar.Core.Providers;

namespace UsageBar.Core.Settings;

internal sealed class SettingsController(
    ISettingsStore settingsStore,
    IUsageRefreshService refreshService,
    IStartupRegistrationService startupRegistration,
    IUpdateService updateService,
    IWindowStartRequestSender windowStartSender,
    IEnumerable<IUsageProvider> providers,
    ILogger<SettingsController> logger)
{
    private readonly IReadOnlyList<string> _iconLayoutKeys = providers
        .OrderBy(provider => provider.Descriptor.DisplayOrder)
        .SelectMany(provider => provider.Descriptor.IconLayoutKeys)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public async Task<SettingsStateMessage> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        var environmentApiKeys = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var provider in settings.Providers ?? [])
        {
            if (provider.Credential is not null &&
                string.IsNullOrWhiteSpace(provider.ApiKey) &&
                Environment.GetEnvironmentVariable(provider.Credential) is { } envValue &&
                !string.IsNullOrWhiteSpace(envValue))
            {
                environmentApiKeys[provider.Credential] = envValue;
            }
        }

        return new SettingsStateMessage(
            "settings-state",
            settings,
            environmentApiKeys,
            _iconLayoutKeys,
            ReadVersion());
    }

    public async Task SaveAsync(
        AppSettings settings,
        IReadOnlyList<string>? environmentSourcedKeys,
        CancellationToken cancellationToken = default)
    {
        var normalized = PreserveEnvironmentKeys(settings, environmentSourcedKeys).Normalize();
        await settingsStore.WriteAsync(normalized, cancellationToken).ConfigureAwait(false);

        if (normalized.StartWithSystem ?? true)
        {
            startupRegistration.Register();
        }
        else
        {
            startupRegistration.Unregister();
        }

        refreshService.RequestManualRefresh();
        logger.LogInformation("Settings saved and applied.");
    }

    public Task SendTestNotificationAsync(CancellationToken cancellationToken = default) =>
        refreshService.SendTestNotificationAsync(cancellationToken);

    public Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default) =>
        updateService.CheckAsync(cancellationToken);

    public async Task<string> TestStartWindowAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        var normalized = settings.Normalize();
        var selector = normalized.Models!.SmallModelSelector;
        var supportedProviders = (normalized.Providers ?? [])
            .Where(provider => provider.Enabled && IsWindowStartProvider(provider))
            .GroupBy(provider => provider.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last().Name)
            .ToArray();
        if (supportedProviders.Length == 0)
        {
            return "No enabled Codex, Claude, or Antigravity provider.";
        }

        var results = new List<string>(supportedProviders.Length);
        foreach (var providerName in supportedProviders)
        {
            try
            {
                await windowStartSender.StartAsync(providerName, selector, cancellationToken).ConfigureAwait(false);
                results.Add($"{providerName}: OK");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "{Provider} test start-window request failed.", providerName);
                results.Add($"{providerName}: failed");
            }
        }

        return string.Join(" · ", results);
    }

    private static AppSettings PreserveEnvironmentKeys(
        AppSettings settings,
        IReadOnlyList<string>? environmentSourcedKeys)
    {
        if (environmentSourcedKeys is null || settings.Providers is null)
        {
            return settings;
        }

        var sourced = environmentSourcedKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.Ordinal);
        if (sourced.Count == 0)
        {
            return settings;
        }

        return settings with
        {
            Providers = settings.Providers
                .Select(provider => provider.Credential is not null && sourced.Contains(provider.Credential)
                    ? provider with { ApiKey = null }
                    : provider)
                .ToList(),
        };
    }

    private static bool IsWindowStartProvider(ProviderSettings provider)
    {
        var key = provider.Id ?? provider.Name;
        return key.Equals("codex", StringComparison.OrdinalIgnoreCase)
            || key.Equals("claude", StringComparison.OrdinalIgnoreCase)
            || key.Equals("antigravity", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version is not null && version.Major > 0
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : "1.0.0";
    }
}
