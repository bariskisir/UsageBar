using UsageBar.Core.Application;
using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;
using Xunit;

namespace UsageBar.Tests;

public sealed class ThresholdNotifierTests
{
    private static readonly AppSettings Settings = AppSettings.Default; // high 70, critical 90

    private static IReadOnlyList<UsageWindow> Codex(double percent) => [TestData.Window("Codex", "Session", percent)];

    [Fact]
    public void First_evaluation_skips_without_baseline()
    {
        // On the very first evaluation there is no previous window, so the notifier
        // records the current values as a baseline and emits no notification — a fresh
        // launch should not spam the user. The next refresh compares against real data.
        var notifier = new ThresholdNotifier();
        Assert.Empty(notifier.Evaluate(Codex(80), Settings));

        // Second evaluation: 80% → 95% crosses the critical (90%) threshold.
        var crossed = notifier.Evaluate(Codex(95), Settings);
        var notification = Assert.Single(crossed);
        Assert.Equal(NotificationLevel.Critical, notification.Level);
    }

    [Fact]
    public void High_threshold_fires_once_then_stays_quiet()
    {
        var notifier = new ThresholdNotifier();
        notifier.Evaluate(Codex(60), Settings);

        var crossed = notifier.Evaluate(Codex(75), Settings);
        var notification = Assert.Single(crossed);
        Assert.Equal(NotificationLevel.High, notification.Level);
        Assert.Contains("approaching limit", notification.Message, StringComparison.Ordinal);

        Assert.Empty(notifier.Evaluate(Codex(80), Settings));
    }

    [Fact]
    public void Critical_threshold_fires_when_crossed()
    {
        var notifier = new ThresholdNotifier();
        notifier.Evaluate(Codex(60), Settings);
        notifier.Evaluate(Codex(75), Settings); // high

        var crossed = notifier.Evaluate(Codex(95), Settings);
        var notification = Assert.Single(crossed);
        Assert.Equal(NotificationLevel.Critical, notification.Level);
        Assert.Contains("critically high", notification.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reaching_100_percent_emits_limit_reached_with_critical_icon_once()
    {
        var notifier = new ThresholdNotifier();
        notifier.Evaluate(Codex(95), Settings); // already critical territory

        var reached = notifier.Evaluate(Codex(100), Settings);
        var notification = Assert.Single(reached);
        Assert.Equal(NotificationLevel.Critical, notification.Level);
        Assert.Contains("limit reached", notification.Message, StringComparison.Ordinal);

        // Fires once: staying at 100 emits nothing further.
        Assert.Empty(notifier.Evaluate(Codex(100), Settings));
    }

    [Fact]
    public void Usage_drop_emits_reset_and_clears_state()
    {
        var notifier = new ThresholdNotifier();
        notifier.Evaluate(Codex(60), Settings);
        notifier.Evaluate(Codex(75), Settings); // high fired

        var reset = notifier.Evaluate(Codex(50), Settings);
        var notification = Assert.Single(reset);
        Assert.Equal(NotificationLevel.Reset, notification.Level);
        Assert.Contains("reset to 50%", notification.Message, StringComparison.Ordinal);

        // State cleared: climbing past high fires again.
        var again = notifier.Evaluate(Codex(75), Settings);
        Assert.Equal(NotificationLevel.High, Assert.Single(again).Level);
    }

    [Fact]
    public void Windows_are_tracked_independently()
    {
        var notifier = new ThresholdNotifier();
        IReadOnlyList<UsageWindow> previous =
            [TestData.Window("Codex", "Session", 60), TestData.Window("Claude", "Weekly", 60)];
        notifier.Evaluate(previous, Settings);

        IReadOnlyList<UsageWindow> current =
            [TestData.Window("Codex", "Session", 95), TestData.Window("Claude", "Weekly", 61)];
        var crossed = notifier.Evaluate(current, Settings);

        var notification = Assert.Single(crossed);
        Assert.Equal(NotificationLevel.Critical, notification.Level);
        Assert.Contains("Codex Session", notification.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Disappearing_window_clears_state_and_fires_fresh_when_reappears()
    {
        var notifier = new ThresholdNotifier();
        notifier.Evaluate(
            [TestData.Window("Codex", "Session", 60)],
            Settings);
        var crossed = notifier.Evaluate(
            [TestData.Window("Codex", "Session", 75)],  // fires High
            Settings);
        Assert.Single(crossed);

        // Codex Session disappears — state should be purged.
        Assert.Empty(notifier.Evaluate(
            [TestData.Window("Claude", "Weekly", 40)],
            Settings));

        // Codex Session reappears — needs a new baseline first, so nothing fires.
        Assert.Empty(notifier.Evaluate(
            [TestData.Window("Codex", "Session", 65)],
            Settings));

        // Now crossing the threshold should fire again because stale state was purged.
        crossed = notifier.Evaluate(
            [TestData.Window("Codex", "Session", 80)],
            Settings);

        var notification = Assert.Single(crossed);
        Assert.Equal(NotificationLevel.High, notification.Level);
        Assert.Contains("Codex Session", notification.Message, StringComparison.Ordinal);
    }
}
