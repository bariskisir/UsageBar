using UsageBar.Application;
using UsageBar.Domain;
using Xunit;

namespace UsageBar.Tests;

public sealed class IconLayoutTests
{
    [Fact]
    public void Codex_pro_and_claude_subscriber_yields_four_bars()
    {
        IReadOnlyList<UsageWindow> windows =
        [
            TestData.Window("Codex", "Session", 10),
            TestData.Window("Codex", "Weekly", 20),
            TestData.Window("Claude", "Session", 30),
            TestData.Window("Claude", "Weekly", 40),
        ];

        var bars = IconLayout.Compute(windows, []);

        Assert.Equal(["Codex", "Codex", "Claude", "Claude"], bars.Select(b => b.Provider));
        Assert.Equal([10.0, 20.0, 30.0, 40.0], bars.Select(b => b.UsedPercent));
    }

    [Fact]
    public void Codex_free_plan_uses_weekly_only_then_claude()
    {
        IReadOnlyList<UsageWindow> windows =
        [
            TestData.Window("Codex", "Weekly", 25),
            TestData.Window("Claude", "Session", 30),
            TestData.Window("Claude", "Weekly", 40),
        ];
        IReadOnlyList<ProviderPlan> plans = [new ProviderPlan("Codex", "Free")];

        var bars = IconLayout.Compute(windows, plans);

        Assert.Equal(["Codex", "Claude", "Claude"], bars.Select(b => b.Provider));
        Assert.Equal([25.0, 30.0, 40.0], bars.Select(b => b.UsedPercent));
    }

    [Fact]
    public void Claude_only_subscriber_yields_two_bars()
    {
        IReadOnlyList<UsageWindow> windows =
        [
            TestData.Window("Claude", "Session", 30),
            TestData.Window("Claude", "Weekly", 40),
        ];

        var bars = IconLayout.Compute(windows, []);

        Assert.Equal(["Claude", "Claude"], bars.Select(b => b.Provider));
    }

    [Fact]
    public void Empty_input_yields_single_empty_bar()
    {
        var bars = IconLayout.Compute([], []);

        var bar = Assert.Single(bars);
        Assert.Null(bar.UsedPercent);
        Assert.Equal("None", bar.Provider);
    }
}
