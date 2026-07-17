using Microsoft.Extensions.Logging.Abstractions;
using UsageBar.Core.Application;
using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;
using Xunit;

namespace UsageBar.Tests;

public sealed class UsageWindowStartServiceTests
{
    [Fact]
    public async Task First_observation_only_arms_provider_then_usage_drop_starts_once()
    {
        var sender = new RecordingSender();
        var service = CreateService(sender);
        var settings = Settings(startWindow: true, selector: "flash,mini");

        await service.ObserveAsync([Session(80)], settings, CancellationToken.None);
        Assert.Empty(sender.Calls);

        await service.ObserveAsync([Session(2)], settings, CancellationToken.None);
        var call = Assert.Single(sender.Calls);
        Assert.Equal("Codex", call.Provider);
        Assert.Equal("flash,mini", call.Selector);

        await service.ObserveAsync([Session(2)], settings, CancellationToken.None);
        Assert.Single(sender.Calls);
    }

    [Fact]
    public async Task Disabled_toggle_never_starts_window()
    {
        var sender = new RecordingSender();
        var service = CreateService(sender);
        var settings = Settings(startWindow: false, selector: "mini");

        await service.ObserveAsync([Session(80)], settings, CancellationToken.None);
        await service.ObserveAsync([Session(2)], settings, CancellationToken.None);

        Assert.Empty(sender.Calls);
    }

    [Fact]
    public async Task Failed_request_is_retried_on_next_refresh()
    {
        var sender = new RecordingSender(failures: 1);
        var service = CreateService(sender);
        var settings = Settings(startWindow: true, selector: "mini");

        await service.ObserveAsync([Session(80)], settings, CancellationToken.None);
        await service.ObserveAsync([Session(2)], settings, CancellationToken.None);
        await service.ObserveAsync([Session(2)], settings, CancellationToken.None);

        Assert.Equal(2, sender.Calls.Count);
    }

    [Fact]
    public async Task Low_usage_and_later_reset_timestamp_across_two_observations_warm_once()
    {
        var sender = new RecordingSender();
        var service = CreateService(sender);
        var settings = Settings(startWindow: true, selector: "mini");
        var resetAt = new DateTimeOffset(2026, 7, 16, 18, 0, 0, TimeSpan.Zero);

        await service.ObserveAsync([Session(2, resetAt)], settings, CancellationToken.None);
        await service.ObserveAsync([Session(2, resetAt.AddMinutes(5))], settings, CancellationToken.None);
        Assert.Single(sender.Calls);

        await service.ObserveAsync([Session(2, resetAt.AddMinutes(10))], settings, CancellationToken.None);
        Assert.Single(sender.Calls);
    }

    [Fact]
    public async Task Reset_timestamp_increase_requires_usage_strictly_below_five()
    {
        var sender = new RecordingSender();
        var service = CreateService(sender);
        var settings = Settings(startWindow: true, selector: "mini");
        var resetAt = new DateTimeOffset(2026, 7, 16, 18, 0, 0, TimeSpan.Zero);

        await service.ObserveAsync([Session(5, resetAt)], settings, CancellationToken.None);
        await service.ObserveAsync([Session(5, resetAt.AddMinutes(5))], settings, CancellationToken.None);
        await service.ObserveAsync([Session(5, resetAt.AddMinutes(10))], settings, CancellationToken.None);
        Assert.Empty(sender.Calls);

        var lowUsageSender = new RecordingSender();
        var lowUsageService = CreateService(lowUsageSender);
        await lowUsageService.ObserveAsync([Session(2, resetAt.AddMinutes(15))], settings, CancellationToken.None);
        await lowUsageService.ObserveAsync([Session(2, resetAt.AddMinutes(15))], settings, CancellationToken.None);
        Assert.Empty(lowUsageSender.Calls);

        await lowUsageService.ObserveAsync([Session(2, resetAt.AddMinutes(20))], settings, CancellationToken.None);
        Assert.Single(lowUsageSender.Calls);
    }

    [Fact]
    public async Task Enabling_mid_window_does_not_send_until_observed_deadline()
    {
        var sender = new RecordingSender();
        var service = CreateService(sender);

        await service.ObserveAsync([Session(80)], Settings(startWindow: false, selector: "mini"), CancellationToken.None);
        await service.ObserveAsync([Session(80)], Settings(startWindow: true, selector: "mini"), CancellationToken.None);

        Assert.Empty(sender.Calls);
        await service.ObserveAsync([Session(2)], Settings(startWindow: true, selector: "mini"), CancellationToken.None);
        Assert.Single(sender.Calls);
    }

    [Fact]
    public async Task Multiple_buckets_warm_independently()
    {
        var sender = new RecordingSender();
        var service = CreateService(sender);
        var settings = AntigravitySettings(startWindow: true, selector: "flash,mini");

        UsageWindow gemini(double pct) => new("Antigravity", "Session", pct, subLabel: "Gemini");
        UsageWindow claude(double pct) => new("Antigravity", "Session", pct, subLabel: "Claude and GPT");

        // First observation arms both buckets.
        await service.ObserveAsync([gemini(80), claude(60)], settings, CancellationToken.None);
        Assert.Empty(sender.Calls);

        // Only Gemini resets — one warm call.
        await service.ObserveAsync([gemini(2), claude(65)], settings, CancellationToken.None);
        Assert.Single(sender.Calls);

        // Claude resets — second warm call.
        await service.ObserveAsync([gemini(5), claude(10)], settings, CancellationToken.None);
        Assert.Equal(2, sender.Calls.Count);
    }

    private static UsageWindowStartService CreateService(IWindowStartRequestSender sender) =>
        new(sender, NullLogger<UsageWindowStartService>.Instance);

    private static UsageWindow Session(double usedPercent, DateTimeOffset? resetAt = null) =>
        new("Codex", "Session", usedPercent, resetAt: resetAt);

    private static AppSettings Settings(bool startWindow, string selector) => AppSettings.Default with
    {
        Models = new ModelSettings(selector),
        Providers =
        [
            new ProviderSettings(
                "Codex",
                ProviderSettings.TypeOAuth,
                null,
                null,
                Enabled: true,
                StartWindowAfterReset: startWindow),
        ],
    };

    private static AppSettings AntigravitySettings(bool startWindow, string selector) => AppSettings.Default with
    {
        Models = new ModelSettings(selector),
        Providers =
        [
            new ProviderSettings(
                "Antigravity",
                ProviderSettings.TypeOAuth,
                null,
                null,
                Enabled: true,
                StartWindowAfterReset: startWindow),
        ],
    };

    private sealed class RecordingSender(int failures = 0) : IWindowStartRequestSender
    {
        private int _remainingFailures = failures;

        public List<(string Provider, string Selector)> Calls { get; } = [];

        public Task StartAsync(string providerName, string smallModelSelector, CancellationToken cancellationToken)
        {
            Calls.Add((providerName, smallModelSelector));
            if (_remainingFailures-- > 0)
            {
                throw new HttpRequestException("transient");
            }

            return Task.CompletedTask;
        }
    }
}
