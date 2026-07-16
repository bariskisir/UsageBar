using UsageBar.Core.Application;
using UsageBar.Core.Domain;
using UsageBar.Core.Providers;

namespace UsageBar.Core.Infrastructure;

/// <summary>
/// Reconciles persisted provider settings with the providers registered in DI. Provider identity,
/// authentication and settings ordering come from each provider descriptor, so adding a provider
/// no longer requires a second hard-coded catalog.
/// </summary>
internal sealed class ProviderInitializer(
    ISettingsStore settingsStore,
    IEnumerable<IUsageProvider> registeredProviders)
{
    private readonly IReadOnlyList<IUsageProvider> _providers = registeredProviders
        .OrderBy(provider => provider.Descriptor.SettingsOrder)
        .ThenBy(provider => provider.Descriptor.DisplayOrder)
        .ToArray();

    public async Task EnsureProvidersAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        var existingProviders = settings.Providers ?? [];
        var byName = new Dictionary<string, ProviderSettings>(StringComparer.OrdinalIgnoreCase);
        foreach (var providerSettings in existingProviders)
        {
            byName[providerSettings.Name] = providerSettings;
            if (!string.IsNullOrWhiteSpace(providerSettings.Id))
            {
                byName[providerSettings.Id] = providerSettings;
            }
        }

        var merged = settings.Initialized == true
            ? new List<ProviderSettings>(existingProviders)
            : [];
        var changed = settings.Initialized != true;

        foreach (var provider in _providers)
        {
            var descriptor = provider.Descriptor;
            if (byName.TryGetValue(descriptor.Id, out var existing) ||
                byName.TryGetValue(descriptor.Name, out existing))
            {
                if (!string.Equals(existing.Id, descriptor.Id, StringComparison.Ordinal))
                {
                    var index = merged.FindIndex(item => ReferenceEquals(item, existing) || item == existing);
                    if (index >= 0)
                    {
                        merged[index] = existing with { Id = descriptor.Id };
                        changed = true;
                    }
                }
                continue;
            }

            merged.Add(new ProviderSettings(
                Name: descriptor.Name,
                Type: SettingsType(descriptor.AuthenticationKind),
                Credential: descriptor.CredentialName,
                ApiKey: null,
                Enabled: HasCredential(provider),
                Id: descriptor.Id,
                StartWindowAfterReset: string.Equals(descriptor.Id, "codex", StringComparison.OrdinalIgnoreCase)));
            changed = true;
        }

        if (changed)
        {
            await settingsStore.WriteAsync(
                settings with { Providers = merged, Initialized = true },
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool HasCredential(IUsageProvider provider)
    {
        try
        {
            var apiKeys = new Dictionary<string, string>(StringComparer.Ordinal);
            if (provider.Descriptor.CredentialName is { } credentialName &&
                Environment.GetEnvironmentVariable(credentialName) is { Length: > 0 } value)
            {
                apiKeys[credentialName] = value;
            }

            return provider.IsConfigured(new ProviderQueryContext(DateTimeOffset.UtcNow, apiKeys));
        }
        catch
        {
            return false;
        }
    }

    private static string SettingsType(ProviderAuthenticationKind kind) => kind == ProviderAuthenticationKind.OAuth
        ? ProviderSettings.TypeOAuth
        : ProviderSettings.TypeApiKey;
}
