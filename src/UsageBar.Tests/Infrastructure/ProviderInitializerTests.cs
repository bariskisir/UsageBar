using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;
using UsageBar.Core.Infrastructure;
using UsageBar.Core.Providers;
using Xunit;

namespace UsageBar.Tests;

public sealed class ProviderInitializerTests
{
    [Fact]
    public async Task Builds_missing_provider_settings_from_registered_descriptors_in_settings_order()
    {
        var settings = new StubSettingsStore(AppSettings.Default with
        {
            Initialized = false,
            Providers = [],
        });
        var providers = new IUsageProvider[]
        {
            new MetadataProvider(new ProviderDescriptor(
                "Balance", 50, ProviderAuthenticationKind.ApiKey, "BALANCE_KEY", SettingsOrder: 2)),
            new MetadataProvider(new ProviderDescriptor(
                "OAuth", 10, ProviderAuthenticationKind.OAuth, SettingsOrder: 1)),
        };

        await new ProviderInitializer(settings, providers).EnsureProvidersAsync();

        var initialized = settings.Current;
        Assert.True(initialized.Initialized);
        Assert.Collection(
            initialized.Providers!,
            oauth =>
            {
                Assert.Equal("OAuth", oauth.Name);
                Assert.Equal("oauth", oauth.Id);
                Assert.Equal(ProviderSettings.TypeOAuth, oauth.Type);
                Assert.Null(oauth.Credential);
                Assert.True(oauth.Enabled);
            },
            apiKey =>
            {
                Assert.Equal("Balance", apiKey.Name);
                Assert.Equal("balance", apiKey.Id);
                Assert.Equal(ProviderSettings.TypeApiKey, apiKey.Type);
                Assert.Equal("BALANCE_KEY", apiKey.Credential);
                Assert.True(apiKey.Enabled);
            });
    }

    private sealed class MetadataProvider(ProviderDescriptor descriptor) : ISingleResultUsageProvider
    {
        public ProviderDescriptor Descriptor { get; } = descriptor;

        public bool IsConfigured(ProviderQueryContext context) => true;

        public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken) =>
            Task.FromResult<ProviderResult?>(null);
    }
}