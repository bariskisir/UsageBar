using UsageBar.Application;
using UsageBar.Domain;
using Xunit;

namespace UsageBar.Tests;

public sealed class IconLayoutTests
{
    private static ProviderResult Metric(string name, params IconBar[] bars) =>
        new MetricResult(name, Plan: null, Windows: [], IconBars: bars);

    [Fact]
    public void Concatenates_metric_bars_in_result_order_with_weights()
    {
        IReadOnlyList<ProviderResult> results =
        [
            Metric("Codex", IconBar.Create(10, 1.0), IconBar.Create(20, 1.0)),
            Metric("Claude", IconBar.Create(30, 1.0), IconBar.Create(40, 1.0)),
        ];

        var bars = IconLayout.Compute(results);

        Assert.Equal(["Codex", "Codex", "Claude", "Claude"], bars.Select(b => b.Provider));
        Assert.Equal([10.0, 20.0, 30.0, 40.0], bars.Select(b => b.UsedPercent));
        Assert.Equal([1.0, 1.0, 1.0, 1.0], bars.Select(b => b.Weight));
    }

    [Fact]
    public void Codex_free_double_weight_bar_then_claude()
    {
        IReadOnlyList<ProviderResult> results =
        [
            Metric("Codex", IconBar.Create(25, 2.0)),
            Metric("Claude", IconBar.Create(30, 1.0), IconBar.Create(40, 1.0)),
        ];

        var bars = IconLayout.Compute(results);

        Assert.Equal(["Codex", "Claude", "Claude"], bars.Select(b => b.Provider));
        Assert.Equal([2.0, 1.0, 1.0], bars.Select(b => b.Weight));
    }

    [Fact]
    public void Balance_results_contribute_no_bars()
    {
        IReadOnlyList<ProviderResult> results = [new BalanceResult("DeepSeek", "$9.99")];

        var bar = Assert.Single(IconLayout.Compute(results));
        Assert.Null(bar.UsedPercent);
        Assert.Equal("None", bar.Provider);
    }

    [Fact]
    public void Empty_input_yields_single_empty_bar()
    {
        var bar = Assert.Single(IconLayout.Compute([]));
        Assert.Null(bar.UsedPercent);
        Assert.Equal("None", bar.Provider);
    }
}
