using Microsoft.Extensions.Logging;
using UsageBar.Core.Configuration;
using UsageBar.Core.Infrastructure;

namespace UsageBar.Core.Application;

internal sealed class DesktopApplicationCoordinator(
    ProviderInitializer providerInitializer,
    ISettingsStore settingsStore,
    IStartupRegistrationService startupRegistration,
    ILogger<DesktopApplicationCoordinator> logger)
{
    public async Task<AppSettings> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await providerInitializer.EnsureProvidersAsync(cancellationToken).ConfigureAwait(false);
        var settings = await settingsStore.ReadAsync(cancellationToken).ConfigureAwait(false);

        if (settings.StartWithSystem ?? true)
        {
            startupRegistration.Register();
        }
        else
        {
            startupRegistration.Unregister();
        }

        logger.LogInformation(
            "Desktop services initialized for settings schema {SchemaVersion}.",
            settings.SchemaVersion);
        return settings;
    }
}