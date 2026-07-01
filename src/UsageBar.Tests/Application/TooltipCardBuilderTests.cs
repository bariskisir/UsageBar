using UsageBar.Application;
using UsageBar.Domain;
using Xunit;

namespace UsageBar.Tests;

public sealed class TooltipCardBuilderTests
{
    [Fact]
    public void Builds_cards_in_result_order_with_correct_shapes()
    {
        IReadOnlyList<ProviderResult> results =
        [
            new MetricResult("Codex", "Pro", [TestData.Window("Codex", "Session", 10, "1h 0m")], []),
            new MetricResult("Claude", "Max", [TestData.Window("Claude", "Session", 50, "2h 0m")], []),
            new BalanceResult("OpenRouter", "$1.00"),
        ];

        var cards = TooltipCardBuilder.Build(new UsageSnapshot(results, []));

        Assert.Equal(["Codex", "Claude", "OpenRouter"], cards.Select(c => c.Title));
    }

    [Fact]
    public void Metric_card_carries_plan_and_metric_rows()
    {
        IReadOnlyList<ProviderResult> results =
        [
            new MetricResult("Codex", "Pro", [TestData.Window("Codex", "Session", 10, "1h 0m")], []),
        ];

        var card = Assert.Single(TooltipCardBuilder.Build(new UsageSnapshot(results, [])));

        Assert.Equal("Pro", card.Plan);
        Assert.Empty(card.Lines);
        var metric = Assert.Single(card.Metrics);
        Assert.Equal("Session", metric.Label);
        Assert.Equal(10, metric.Percent);
        Assert.Equal("1h 0m", metric.Detail);
    }

    [Fact]
    public void Balance_card_has_a_single_line_and_no_plan()
    {
        IReadOnlyList<ProviderResult> results = [new BalanceResult("DeepSeek", "$9.99")];

        var card = Assert.Single(TooltipCardBuilder.Build(new UsageSnapshot(results, [])));

        Assert.Null(card.Plan);
        Assert.Empty(card.Metrics);
        Assert.Equal(["$9.99"], card.Lines);
    }

    [Fact]
    public void Metric_result_with_no_windows_is_skipped()
    {
        IReadOnlyList<ProviderResult> results =
        [
            new MetricResult("Codex", "Pro", [], []),
            new BalanceResult("DeepSeek", "$9.99"),
        ];

        var card = Assert.Single(TooltipCardBuilder.Build(new UsageSnapshot(results, [])));
        Assert.Equal("DeepSeek", card.Title);
    }

    [Fact]
    public void Balance_card_is_not_hidden_when_threshold_is_disabled()
    {
        IReadOnlyList<ProviderResult> results = [new BalanceResult("OpenRouter", "$0.00", UsdAmount: 0)];

        var card = Assert.Single(TooltipCardBuilder.Build(new UsageSnapshot(results, []), balanceHidingThreshold: -1));

        Assert.False(card.Hide);
    }

    [Fact]
    public void Balance_card_is_hidden_when_at_or_below_threshold()
    {
        IReadOnlyList<ProviderResult> results = [new BalanceResult("OpenRouter", "$0.00", UsdAmount: 0)];

        var card = Assert.Single(TooltipCardBuilder.Build(new UsageSnapshot(results, []), balanceHidingThreshold: 0));

        Assert.True(card.Hide);
    }

    [Fact]
    public void Balance_card_is_not_hidden_when_above_threshold()
    {
        IReadOnlyList<ProviderResult> results = [new BalanceResult("OpenRouter", "$5.00", UsdAmount: 5)];

        var card = Assert.Single(TooltipCardBuilder.Build(new UsageSnapshot(results, []), balanceHidingThreshold: 0));

        Assert.False(card.Hide);
    }

    [Fact]
    public void DeepSeek_hide_requires_both_usd_and_cny_at_or_below_threshold()
    {
        // USD is 0 (≤0) but CNY is 10 (>0) — should NOT hide.
        IReadOnlyList<ProviderResult> results = [new BalanceResult("DeepSeek", "$0.00 / ¥10.00", UsdAmount: 0, CnyAmount: 10)];

        var card = Assert.Single(TooltipCardBuilder.Build(new UsageSnapshot(results, []), balanceHidingThreshold: 0));

        Assert.False(card.Hide);
    }

    [Fact]
    public void DeepSeek_hides_when_both_balances_are_at_or_below_threshold()
    {
        // Both USD and CNY are ≤0 — should hide.
        IReadOnlyList<ProviderResult> results = [new BalanceResult("DeepSeek", "$0.00 / ¥0.00", UsdAmount: 0, CnyAmount: 0)];

        var card = Assert.Single(TooltipCardBuilder.Build(new UsageSnapshot(results, []), balanceHidingThreshold: 0));

        Assert.True(card.Hide);
    }
}
