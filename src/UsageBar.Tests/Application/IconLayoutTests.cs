using UsageBar.Core.Application;
using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;
using Xunit;

namespace UsageBar.Tests;

public sealed class IconLayoutTests
{
    private static ProviderResult Metric(string name, params UsageWindow[] windows) =>
        new MetricResult(name, Plan: null, Windows: windows);

    [Fact]
    public void Auto_layout_shows_all_metric_windows_equally_in_result_order()
    {
        IReadOnlyList<ProviderResult> results =
        [
            Metric("Codex", TestData.Window("Codex", "Session", 10), TestData.Window("Codex", "Weekly", 20)),
            Metric("Claude", TestData.Window("Claude", "Session", 30), TestData.Window("Claude", "Weekly", 40)),
            Metric("ElevenLabs", TestData.Window("ElevenLabs", "Session", 50)),
        ];

        var bars = IconLayout.Compute(results, TrayIconLayoutSettings.Default);

        Assert.Equal(["Codex", "Codex", "Claude", "Claude", "ElevenLabs"], bars.Select(b => b.Provider));
        Assert.Equal([10.0, 20.0, 30.0, 40.0, 50.0], bars.Select(b => b.UsedPercent));
        Assert.Equal([1.0, 1.0, 1.0, 1.0, 1.0], bars.Select(b => b.Weight));
    }

    [Fact]
    public void Manual_layout_shows_only_configured_windows_in_configured_order_and_weight()
    {
        IReadOnlyList<ProviderResult> results =
        [
            Metric("Codex", TestData.Window("Codex", "Session", 10), TestData.Window("Codex", "Weekly", 20)),
            Metric("Claude", TestData.Window("Claude", "Session", 30), TestData.Window("Claude", "Weekly", 40)),
            Metric("ElevenLabs", TestData.Window("ElevenLabs", "Session", 50)),
        ];
        var settings = new TrayIconLayoutSettings(
            TrayIconLayoutSettings.ManualMode,
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["claude_weekly"] = 50,
                ["codex_session"] = 25,
                ["elevenlabs_session"] = 25,
            });

        var bars = IconLayout.Compute(results, settings);

        Assert.Equal(["Claude", "Codex", "ElevenLabs"], bars.Select(b => b.Provider));
        Assert.Equal([40.0, 10.0, 50.0], bars.Select(b => b.UsedPercent));
        Assert.Equal([50.0, 25.0, 25.0], bars.Select(b => b.Weight));
    }

    [Fact]
    public void Manual_layout_omits_windows_not_in_settings()
    {
        IReadOnlyList<ProviderResult> results =
        [
            Metric("Codex", TestData.Window("Codex", "Session", 10), TestData.Window("Codex", "Weekly", 20)),
            Metric("Claude", TestData.Window("Claude", "Session", 30)),
        ];
        var settings = new TrayIconLayoutSettings(
            TrayIconLayoutSettings.ManualMode,
            new Dictionary<string, double> { ["codex_weekly"] = 100 });

        var bar = Assert.Single(IconLayout.Compute(results, settings));

        Assert.Equal("Codex", bar.Provider);
        Assert.Equal(20, bar.UsedPercent);
        Assert.Equal(100, bar.Weight);
    }

    [Fact]
    public void Manual_layout_leaves_unassigned_weight_empty_at_the_bottom()
    {
        IReadOnlyList<ProviderResult> results =
        [
            Metric("Codex", TestData.Window("Codex", "Session", 10), TestData.Window("Codex", "Weekly", 20)),
        ];
        var settings = new TrayIconLayoutSettings(
            TrayIconLayoutSettings.ManualMode,
            new Dictionary<string, double>
            {
                ["codex_session"] = 10,
                ["codex_weekly"] = 10,
            });

        var bars = IconLayout.Compute(results, settings);

        Assert.Equal(["Codex", "Codex", "None"], bars.Select(b => b.Provider));
        Assert.Equal([10.0, 20.0, null], bars.Select(b => b.UsedPercent));
        Assert.Equal([10.0, 10.0, 80.0], bars.Select(b => b.Weight));
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

    [Theory]
    [InlineData("Codex", "Session", "codex_session")]
    [InlineData("ElevenLabs", "Session", "elevenlabs_session")]
    [InlineData("My Provider", "Weekly Limit", "my_provider_weekly_limit")]
    public void Window_keys_are_stable(string provider, string label, string expected)
    {
        Assert.Equal(expected, IconLayout.WindowKey(provider, label));
    }
}