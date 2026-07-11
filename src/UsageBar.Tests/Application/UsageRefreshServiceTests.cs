using Microsoft.Extensions.Logging.Abstractions;
using UsageBar.Core.Application;
using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;
using UsageBar.Core.Providers;
using Xunit;

namespace UsageBar.Tests;
public sealed class UsageRefreshServiceTests
{
    [Fact]
    public async Task SendTestNotification_delegates_to_dispatcher()
    {
        var dispatcher = new GateDispatcher();
        var service = CreateService(dispatcher: dispatcher);
        await service.SendTestNotificationAsync();
        Assert.True(dispatcher.TestNotificationSent);
    }

    [Fact]
    public async Task Start_triggers_refresh_and_updates_view()
    {
        var settings = new StubSettingsStore(AppSettings.Default with { Refresh = new RefreshSettings(60) });
        var provider = new StubProvider("Codex", () => new MetricResult("Codex", "Pro", [TestData.Window("Codex", "Session", 25)]));
        var view = new RecordingUsageView();
        var dispatcher = new GateDispatcher();
        var service = CreateService(providers: [provider], settings: settings, view: view, dispatcher: dispatcher);
        using (var cancellation = new CancellationTokenSource())
        {
            var runTask = service.RunAsync(cancellation.Token);
            // Wait for the refresh to reach the dispatcher (last async step).
            await dispatcher.WaitForEmitAsync(TimeSpan.FromSeconds(3));
            Assert.NotEmpty(view.IconBars);
            Assert.NotEmpty(view.Cards);
            Assert.NotEmpty(dispatcher.EmitCalls);
            await StopAsync(cancellation, runTask);
        }
    }

    [Fact]
    public async Task Start_skips_unconfigured_providers()
    {
        var settings = new StubSettingsStore(AppSettings.Default with { Refresh = new RefreshSettings(60) });
        var configured = new StubProvider("DeepSeek", () => new BalanceResult("DeepSeek", "$5.00"));
        var skipped = new StubProvider("Skipped", () => null);
        var view = new RecordingUsageView();
        var dispatcher = new GateDispatcher();
        var service = CreateService(providers: [configured, skipped], settings: settings, view: view, dispatcher: dispatcher);
        using (var cancellation = new CancellationTokenSource())
        {
            var runTask = service.RunAsync(cancellation.Token);
            await dispatcher.WaitForEmitAsync(TimeSpan.FromSeconds(3));
            Assert.NotEmpty(view.Cards);
            await StopAsync(cancellation, runTask);
        }
    }

    [Fact]
    public async Task Cancellation_stops_refresh_loop()
    {
        var settings = new StubSettingsStore(AppSettings.Default with { Refresh = new RefreshSettings(60) });
        var provider = new StubProvider("Codex", () => null);
        var service = CreateService(providers: [provider], settings: settings);
        using (var cancellation = new CancellationTokenSource())
        {
            var runTask = service.RunAsync(cancellation.Token);
            cancellation.Cancel();
            await runTask;
        }
    }

    [Fact]
    public void TriggerManualRefresh_does_not_crash_when_idle()
    {
        var settings = new StubSettingsStore(AppSettings.Default);
        var service = CreateService(settings: settings);
        // Should not throw.
        service.RequestManualRefresh();
    }

    [Fact]
    public async Task TriggerManualRefresh_fires_immediate_refresh()
    {
        var clock = new StubClock(TestData.FixedNow);
        var settings = new StubSettingsStore(AppSettings.Default with { Refresh = new RefreshSettings(60) });
        var provider = new StubProvider("Codex", () => new MetricResult("Codex", "Pro", []));
        var view = new RecordingUsageView();
        var dispatcher = new GateDispatcher();
        var service = CreateService(providers: [provider], settings: settings, clock: clock, view: view, dispatcher: dispatcher);
        using (var cancellation = new CancellationTokenSource())
        {
            var runTask = service.RunAsync(cancellation.Token);
            await dispatcher.WaitForEmitCountAsync(1, TimeSpan.FromSeconds(3));
            service.RequestManualRefresh();
            await dispatcher.WaitForEmitCountAsync(2, TimeSpan.FromSeconds(3));
            // After manual refresh, manual trigger time is recorded.
            // Verify the latest manual refresh time is close to FixedNow.
            Assert.Equal(TestData.FixedNow, clock.LastSetNow);
            await StopAsync(cancellation, runTask);
        }
    }

    [Fact]
    public async Task Provider_failure_is_isolated()
    {
        var settings = new StubSettingsStore(AppSettings.Default with { Refresh = new RefreshSettings(60) });
        var good = new StubProvider("DeepSeek", () => new BalanceResult("DeepSeek", "$5.00"), 100);
        var broken = new StubProvider("Broken", () => throw new ProviderException("boom"));
        var view = new RecordingUsageView();
        var dispatcher = new GateDispatcher();
        var service = CreateService(providers: [good, broken], settings: settings, view: view, dispatcher: dispatcher);
        using (var cancellation = new CancellationTokenSource())
        {
            var runTask = service.RunAsync(cancellation.Token);
            await dispatcher.WaitForEmitAsync(TimeSpan.FromSeconds(3));
            // The good provider should still produce results.
            Assert.Contains(view.Cards, card => card.Title == "DeepSeek");
            await StopAsync(cancellation, runTask);
        }
    }

    [Fact]
    public async Task Refresh_includes_threshold_notifications()
    {
        var clock = new StubClock(TestData.FixedNow);
        var settings = new StubSettingsStore(AppSettings.Default with { Refresh = new RefreshSettings(60) });
        var provider = new StubProvider("Codex", () => new MetricResult("Codex", "Pro", [TestData.Window("Codex", "Session", 80)]));
        var view = new RecordingUsageView();
        var dispatcher = new GateDispatcher();
        var service = CreateService(providers: [provider], settings: settings, clock: clock, view: view, dispatcher: dispatcher);
        using (var cancellation = new CancellationTokenSource())
        {
            var runTask = service.RunAsync(cancellation.Token);
            await dispatcher.WaitForEmitAsync(TimeSpan.FromSeconds(3));
            // Windows are forwarded to the dispatcher for threshold evaluation.
            var emit = Assert.Single(dispatcher.EmitCalls);
            var window = Assert.Single(emit.Windows);
            Assert.Equal("Codex", window.ProviderName);
            Assert.Equal("Session", window.Label);
            Assert.Equal(80, window.UsedPercent);
            await StopAsync(cancellation, runTask);
        }
    }

    [Fact]
    public async Task Refresh_applies_manual_icon_layout_from_settings()
    {
        var settings = new StubSettingsStore(AppSettings.Default with { Refresh = new RefreshSettings(60), Visual = new VisualSettings(Scale: 100, IconLayout: new TrayIconLayoutSettings(TrayIconLayoutSettings.ManualMode, new Dictionary<string, double> { ["codex_weekly"] = 75, ["codex_session"] = 25, })), });
        var provider = new StubProvider("Codex", () => new MetricResult("Codex", "Pro", [TestData.Window("Codex", "Session", 10), TestData.Window("Codex", "Weekly", 90), ]));
        var view = new RecordingUsageView();
        var dispatcher = new GateDispatcher();
        var service = CreateService(providers: [provider], settings: settings, view: view, dispatcher: dispatcher);
        using (var cancellation = new CancellationTokenSource())
        {
            var runTask = service.RunAsync(cancellation.Token);
            await dispatcher.WaitForEmitAsync(TimeSpan.FromSeconds(3));
            var bars = Assert.Single(view.IconBars);
            Assert.Equal(2, bars.Count);
            Assert.Contains(bars, b => b.UsedPercent == 10.0 && b.Weight == 25.0);
            Assert.Contains(bars, b => b.UsedPercent == 90.0 && b.Weight == 75.0);
            await StopAsync(cancellation, runTask);
        }
    }

    private static async Task StopAsync(CancellationTokenSource cancellation, Task runTask)
    {
        cancellation.Cancel();
        await runTask;
    }

    private static UsageRefreshService CreateService(IEnumerable<IUsageProvider>? providers = null, ISettingsStore? settings = null, IUsageView? view = null, IClock? clock = null, IThresholdNotificationDispatcher? dispatcher = null)
    {
        var resolvedProviders = providers ?? [];
        var resolvedSettings = settings ?? new StubSettingsStore(AppSettings.Default);
        var resolvedView = view ?? new RecordingUsageView();
        var resolvedClock = clock ?? new StubClock(TestData.FixedNow);
        var resolvedDispatcher = dispatcher ?? new GateDispatcher();
        var options = UsageRefreshOptions.Default;
        var cycleRunner = new RefreshCycleRunner(resolvedProviders, resolvedSettings, resolvedView, resolvedClock, new StubProviderQueryContextFactory(), options, new UsageAggregator(options, NullLogger<UsageAggregator>.Instance), resolvedDispatcher, NullLogger<RefreshCycleRunner>.Instance);
        return new UsageRefreshService(cycleRunner, resolvedClock, NullLogger<UsageRefreshService>.Instance);
    }

    private sealed class StubClock : IClock
    {
        private readonly DateTimeOffset _now;
        public StubClock(DateTimeOffset now) => _now = now;
        public DateTimeOffset LastSetNow { get; private set; }

        public DateTimeOffset Now
        {
            get
            {
                LastSetNow = _now;
                return _now;
            }
        }
    }

    private sealed class RecordingUsageView : IUsageView
    {
        public List<IReadOnlyList<IconLayout.Bar>> IconBars { get; } = [];
        public List<TooltipCard> Cards { get; } = [];
        public List<(NotificationLevel Level, string Message)> Notifications { get; } = [];

        public void ShowIcon(IReadOnlyList<IconLayout.Bar> bars) => IconBars.Add(bars);
        public void ShowCards(IReadOnlyList<TooltipCard> cards, int scale) => Cards.AddRange(cards);
        public void Notify(NotificationLevel level, string message) => Notifications.Add((level, message));
    }

    private sealed class StubProviderQueryContextFactory : IProviderQueryContextFactory
    {
        public ProviderQueryContext Create(AppSettings settings, DateTimeOffset now) => ProviderQueryContext.FromSettings(settings, now, _ => null);
    }

    private sealed class GateDispatcher : IThresholdNotificationDispatcher
    {
        private readonly TaskCompletionSource _tcs = new();
        public List<(IReadOnlyList<UsageWindow> Windows, AppSettings Settings)> EmitCalls { get; } = [];
        public bool TestNotificationSent { get; private set; }

        public Task SendTestNotificationAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            TestNotificationSent = true;
            return Task.CompletedTask;
        }

        public Task EmitAsync(IReadOnlyList<UsageWindow> windows, AppSettings settings, CancellationToken cancellationToken = default)
        {
            EmitCalls.Add((windows, settings));
            _tcs.TrySetResult();
            return Task.CompletedTask;
        }

        public async Task WaitForEmitAsync(TimeSpan timeout)
        {
            var completed = await Task.WhenAny(_tcs.Task, Task.Delay(timeout));
            if (completed != _tcs.Task)
            {
                throw new TimeoutException("Refresh did not complete within the timeout.");
            }
        }

        public async Task WaitForEmitCountAsync(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (EmitCalls.Count < count && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            if (EmitCalls.Count < count)
            {
                throw new TimeoutException($"Refresh did not emit {count} times within the timeout.");
            }
        }
    }
}