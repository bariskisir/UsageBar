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
    public void High_threshold_fires_only_when_crossed_from_below()
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
    public void Usage_drop_emits_reset_independently_of_thresholds()
    {
        var notifier = new ThresholdNotifier();
        notifier.Evaluate(Codex(10), Settings);

        var reset = notifier.Evaluate(Codex(5), Settings);
        var notification = Assert.Single(reset);
        Assert.Equal(NotificationLevel.Reset, notification.Level);
        Assert.Contains("reset to 5%", notification.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Usage_drop_emits_reset_when_initial_baseline_was_already_above_thresholds()
    {
        var notifier = new ThresholdNotifier();
        Assert.Empty(notifier.Evaluate(Codex(95), Settings));

        var reset = Assert.Single(notifier.Evaluate(Codex(0), Settings));
        Assert.Equal(NotificationLevel.Reset, reset.Level);
        Assert.Contains("reset to 0%", reset.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reset_timestamp_advance_confirms_reset_above_low_usage_cutoff()
    {
        var notifier = new ThresholdNotifier();
        var previousResetAt = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        notifier.Evaluate(
            [new UsageWindow("Antigravity", "Session", 80, subLabel: "Gemini", resetAt: previousResetAt)],
            Settings);

        var reset = Assert.Single(notifier.Evaluate(
            [new UsageWindow("Antigravity", "Session", 8, subLabel: "Gemini", resetAt: previousResetAt.AddHours(5))],
            Settings));

        Assert.Equal(NotificationLevel.Reset, reset.Level);
        Assert.Contains("Antigravity Session (Gemini) reset to 8%", reset.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Partial_usage_drop_without_reset_evidence_is_ignored()
    {
        var notifier = new ThresholdNotifier();
        var resetAt = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        notifier.Evaluate(
            [new UsageWindow("Antigravity", "Weekly", 66, resetAt: resetAt)],
            Settings);

        var notifications = notifier.Evaluate(
            [new UsageWindow("Antigravity", "Weekly", 34, resetAt: resetAt)],
            Settings);

        Assert.Empty(notifications);
    }

    [Fact]
    public void Windows_are_tracked_independently()
    {
        var notifier = new ThresholdNotifier();
        IReadOnlyList<UsageWindow> previous =
        [
            TestData.Window("Codex", "Session", 60, null, "Shared"),
            TestData.Window("Claude", "Session", 60, null, "Shared"),
        ];
        notifier.Evaluate(previous, Settings);

        IReadOnlyList<UsageWindow> current =
        [
            TestData.Window("Codex", "Session", 95, null, "Shared"),
            TestData.Window("Claude", "Session", 61, null, "Shared"),
        ];
        var crossed = notifier.Evaluate(current, Settings);

        var notification = Assert.Single(crossed);
        Assert.Equal(NotificationLevel.Critical, notification.Level);
        Assert.Contains("Codex Session", notification.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reappearing_window_needs_a_fresh_baseline_before_crossing_threshold()
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

        // Now crossing the threshold should fire against the fresh baseline.
        crossed = notifier.Evaluate(
            [TestData.Window("Codex", "Session", 80)],
            Settings);

        var notification = Assert.Single(crossed);
        Assert.Equal(NotificationLevel.High, notification.Level);
        Assert.Contains("Codex Session", notification.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Matching_bar_names_are_scoped_by_provider_for_high_and_reset()
    {
        var notifier = new ThresholdNotifier();
        notifier.Evaluate(
            [
                TestData.Window("Codex", "Session", 95, null, "Shared"),
                TestData.Window("Claude", "Session", 10, null, "Shared"),
            ],
            Settings);

        var notifications = notifier.Evaluate(
            [
                TestData.Window("Codex", "Session", 95, null, "Shared"),
                TestData.Window("Claude", "Session", 75, null, "Shared"),
            ],
            Settings);
        var high = Assert.Single(notifications);
        Assert.Equal(NotificationLevel.High, high.Level);
        Assert.Contains("Claude", high.Message, StringComparison.Ordinal);

        notifications = notifier.Evaluate(
            [
                TestData.Window("Codex", "Session", 95, null, "Shared"),
                TestData.Window("Claude", "Session", 2, null, "Shared"),
            ],
            Settings);
        var reset = Assert.Single(notifications);
        Assert.Equal(NotificationLevel.Reset, reset.Level);
        Assert.Contains("Claude", reset.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Different_sublabels_tracked_independently()
    {
        var notifier = new ThresholdNotifier();
        IReadOnlyList<UsageWindow> baseline =
        [
            TestData.Window("Antigravity", "Session", 60, null, "Gemini"),
            TestData.Window("Antigravity", "Session", 60, null, "Claude and GPT"),
        ];
        notifier.Evaluate(baseline, Settings);

        // Only Gemini crosses high — Claude and GPT stays low.
        IReadOnlyList<UsageWindow> mixed =
        [
            TestData.Window("Antigravity", "Session", 80, null, "Gemini"),
            TestData.Window("Antigravity", "Session", 61, null, "Claude and GPT"),
        ];
        var crossed = notifier.Evaluate(mixed, Settings);
        var notification = Assert.Single(crossed);
        Assert.Equal(NotificationLevel.High, notification.Level);
        Assert.Contains("Gemini", notification.Message, StringComparison.Ordinal);

        // Gemini resets, Claude and GPT crosses high — both fire independently.
        IReadOnlyList<UsageWindow> swapped =
        [
            TestData.Window("Antigravity", "Session", 3, null, "Gemini"),
            TestData.Window("Antigravity", "Session", 80, null, "Claude and GPT"),
        ];
        var result = notifier.Evaluate(swapped, Settings);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, n => n.Level == NotificationLevel.Reset && n.Message.Contains("Gemini", StringComparison.Ordinal));
        Assert.Contains(result, n => n.Level == NotificationLevel.High && n.Message.Contains("Claude and GPT", StringComparison.Ordinal));

        // Only Gemini crosses critical — the other bucket remains unchanged.
        result = notifier.Evaluate(
            [
                TestData.Window("Antigravity", "Session", 95, null, "Gemini"),
                TestData.Window("Antigravity", "Session", 80, null, "Claude and GPT"),
            ],
            Settings);
        var critical = Assert.Single(result);
        Assert.Equal(NotificationLevel.Critical, critical.Level);
        Assert.Contains("Gemini", critical.Message, StringComparison.Ordinal);
    }
}
