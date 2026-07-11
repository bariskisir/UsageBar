using Microsoft.Extensions.Logging.Abstractions;
using UsageBar.Core.Application;
using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;
using UsageBar.Core.Infrastructure;
using UsageBar.Core.Settings;
using Xunit;

namespace UsageBar.Tests;

public sealed class SettingsControllerTests
{
    [Fact]
    public async Task State_exposes_environment_presence_without_the_secret()
    {
        const string credential = "USAGEBAR_SETTINGS_CONTROLLER_TEST_KEY";
        var previous = Environment.GetEnvironmentVariable(credential);
        Environment.SetEnvironmentVariable(credential, "super-secret");
        try
        {
            var settings = AppSettings.Default with
            {
                Providers = [new ProviderSettings("Test", ProviderSettings.TypeApiKey, credential, null, true)],
            };
            var controller = CreateController(new StubSettingsStore(settings));

            var state = await controller.GetStateAsync();

            Assert.True(state.EnvironmentApiKeys[credential]);
            Assert.DoesNotContain("super-secret", System.Text.Json.JsonSerializer.Serialize(state));
        }
        finally
        {
            Environment.SetEnvironmentVariable(credential, previous);
        }
    }

    [Fact]
    public async Task Save_preserves_environment_sourced_key_and_applies_startup_setting()
    {
        const string credential = "TEST_KEY";
        var store = new StubSettingsStore(AppSettings.Default);
        var startup = new RecordingStartupRegistration();
        var controller = CreateController(store, startup);
        var settings = AppSettings.Default with
        {
            StartWithSystem = false,
            Providers = [new ProviderSettings("Test", ProviderSettings.TypeApiKey, credential, "masked", true)],
        };

        await controller.SaveAsync(settings, [credential]);

        Assert.Null(Assert.Single(store.Current.Providers!).ApiKey);
        Assert.True(startup.Unregistered);
    }

    private static SettingsController CreateController(
        ISettingsStore store,
        IStartupRegistrationService? startup = null) =>
        new(
            store,
            new StubRefreshService(),
            startup ?? new RecordingStartupRegistration(),
            new StubUpdateService(),
            [],
            NullLogger<SettingsController>.Instance);

    private sealed class RecordingStartupRegistration : IStartupRegistrationService
    {
        public bool Unregistered { get; private set; }

        public void Register()
        {
        }

        public void Unregister() => Unregistered = true;
    }

    private sealed class StubRefreshService : IUsageRefreshService
    {
        public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void RequestManualRefresh()
        {
        }

        public Task SendTestNotificationAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubUpdateService : IUpdateService
    {
        public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UpdateCheckResult(false, "1.0.0", null));
    }
}