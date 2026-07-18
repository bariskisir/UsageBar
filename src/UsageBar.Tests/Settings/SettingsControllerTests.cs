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
    public async Task State_exposes_environment_value_for_display()
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

            Assert.Equal("super-secret", state.EnvironmentApiKeys[credential]);
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

    [Fact]
    public async Task Test_start_window_uses_shared_selector_for_each_enabled_supported_provider()
    {
        var sender = new RecordingWindowStartSender("Claude");
        var controller = CreateController(new StubSettingsStore(AppSettings.Default), windowStartSender: sender);
        var settings = AppSettings.Default with
        {
            Models = new ModelSettings("flash,mini"),
            Providers =
            [
                new ProviderSettings("Codex", ProviderSettings.TypeOAuth, null, null, Enabled: true, Id: "codex"),
                new ProviderSettings("Claude", ProviderSettings.TypeOAuth, null, null, Enabled: true, Id: "claude"),
                new ProviderSettings("Antigravity", ProviderSettings.TypeOAuth, null, null, Enabled: false, Id: "antigravity"),
                new ProviderSettings("DeepSeek", ProviderSettings.TypeApiKey, "KEY", "value", Enabled: true, Id: "deepseek"),
            ],
        };

        var result = await controller.TestStartWindowAsync(settings);

        Assert.Collection(
            sender.Calls,
            request =>
            {
                Assert.Equal("Codex", request.ProviderName);
                Assert.Equal("flash,mini", request.SmallModelSelector);
                Assert.Null(request.WindowLabel);
                Assert.Null(request.WindowSubLabel);
            },
            request =>
            {
                Assert.Equal("Claude", request.ProviderName);
                Assert.Equal("flash,mini", request.SmallModelSelector);
                Assert.Null(request.WindowLabel);
                Assert.Null(request.WindowSubLabel);
            });
        Assert.Contains("Codex: OK", result, StringComparison.Ordinal);
        Assert.Contains("Claude: failed", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Antigravity", result, StringComparison.Ordinal);
    }

    private static SettingsController CreateController(
        ISettingsStore store,
        IStartupRegistrationService? startup = null,
        IWindowStartRequestSender? windowStartSender = null) =>
        new(
            store,
            new StubRefreshService(),
            startup ?? new RecordingStartupRegistration(),
            new StubUpdateService(),
            windowStartSender ?? new RecordingWindowStartSender(),
            [],
            NullLogger<SettingsController>.Instance);

    private sealed class RecordingWindowStartSender(string? failingProvider = null) : IWindowStartRequestSender
    {
        public List<WindowStartRequest> Calls { get; } = [];

        public Task StartAsync(WindowStartRequest request, CancellationToken cancellationToken)
        {
            Calls.Add(request);
            return string.Equals(request.ProviderName, failingProvider, StringComparison.OrdinalIgnoreCase)
                ? Task.FromException(new HttpRequestException("test failure"))
                : Task.CompletedTask;
        }
    }

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
