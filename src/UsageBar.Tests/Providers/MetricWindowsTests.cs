using UsageBar.Core.Domain;
using UsageBar.Core.Providers;
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
}