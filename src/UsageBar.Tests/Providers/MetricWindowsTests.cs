using UsageBar.Domain;
using UsageBar.Providers;
using Xunit;

namespace UsageBar.Tests;

public sealed class MetricWindowsTests
{
    [Fact]
    public void Require_returns_present_windows_in_order()
    {
        var session = TestData.Window("Codex", "Session", 10);
        var weekly = TestData.Window("Codex", "Weekly", 20);

        var windows = MetricWindows.Require("Codex", session, weekly);

        Assert.Collection(
            windows,
            window => Assert.Equal("Session", window.Label),
            window => Assert.Equal("Weekly", window.Label));
    }

    [Fact]
    public void Require_skips_missing_windows()
    {
        var weekly = TestData.Window("Codex", "Weekly", 20);

        var windows = MetricWindows.Require("Codex", null, weekly);

        Assert.Equal("Weekly", Assert.Single(windows).Label);
    }

    [Fact]
    public void Require_throws_when_no_windows_present()
    {
        var exception = Assert.Throws<ProviderException>(() => MetricWindows.Require("Codex", null, null));
        Assert.Contains("Codex", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EqualWeightBars_builds_one_weight1_bar_per_present_window()
    {
        var session = TestData.Window("Claude", "Session", 88);
        var weekly = TestData.Window("Claude", "Weekly", 40);

        var bars = MetricWindows.EqualWeightBars(session, weekly);

        Assert.Collection(
            bars,
            bar => { Assert.Equal(88.0, bar.UsedPercent); Assert.Equal(1.0, bar.Weight); },
            bar => { Assert.Equal(40.0, bar.UsedPercent); Assert.Equal(1.0, bar.Weight); });
    }

    [Fact]
    public void EqualWeightBars_skips_missing_windows()
    {
        var weekly = TestData.Window("Claude", "Weekly", 40);

        var bars = MetricWindows.EqualWeightBars(null, weekly);

        var bar = Assert.Single(bars);
        Assert.Equal(40.0, bar.UsedPercent);
    }
}
