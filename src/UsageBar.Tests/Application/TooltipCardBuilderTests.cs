using UsageBar.Core.Application;
using UsageBar.Core.Domain;
using Xunit;

namespace UsageBar.Tests;

public sealed class TooltipCardBuilderTests
{
    [Fact]
    public void Builds_cards_in_result_order_with_correct_shapes()
    {
        IReadOnlyList<ProviderResult> results =
        [
            new MetricResult("Codex", "Pro", [TestData.Window("Codex", "Session", 10, "1h 0m")]),
            new MetricResult("Claude", "Max", [TestData.Window("Claude", "Session", 50, "2h 0m")]),
            new BalanceResult("OpenRouter", "$1.00"),
        ];

        var cards = TooltipCardBuilder.Build(
            new UsageSnapshot(results, []),
            new Dictionary<string, string?>
            {
                ["Codex"] = "openai",
                ["Claude"] = "claude",
            });

        Assert.Equal(["Codex", "Claude", "OpenRouter"], cards.Select(c => c.Title));
        Assert.Equal("openai", cards[0].IconKey);
        Assert.Equal("claude", cards[1].IconKey);
        Assert.Null(cards[2].IconKey);
    }

    [Fact]
    public void Metric_card_carries_plan_and_metric_rows()
    {
        IReadOnlyList<ProviderResult> results =
        [
            new MetricResult("Codex", "Pro", [TestData.Window("Codex", "Session", 10, "1h 0m")]),
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
    public void Metric_card_carries_card_level_notice()
    {
        IReadOnlyList<ProviderResult> results =
        [
            new MetricResult("Codex", "Pro", [TestData.Window("Codex", "Session", 10)], "1 reset"),
        ];

        var card = Assert.Single(TooltipCardBuilder.Build(new UsageSnapshot(results, [])));

        Assert.Equal("1 reset", card.Notice);
        Assert.Null(Assert.Single(card.Metrics).SubLabel);
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
            new MetricResult("Codex", "Pro", []),
            new BalanceResult("DeepSeek", "$9.99"),
        ];

        var card = Assert.Single(TooltipCardBuilder.Build(new UsageSnapshot(results, [])));
        Assert.Equal("DeepSeek", card.Title);
    }

}
