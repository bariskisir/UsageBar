using UsageBar.Application;
using UsageBar.Domain;
using Xunit;

namespace UsageBar.Tests;

public sealed class TooltipCardBuilderTests
{
    [Fact]
    public void Orders_metric_cards_before_balance_and_codex_before_claude()
    {
        IReadOnlyList<ProviderResult> results =
        [
            ProviderResult.Balance("OpenRouter", "$1.00"),
            ProviderResult.Metric("Claude", "Max", [TestData.Window("Claude", "Session", 50, "2h 0m")]),
            ProviderResult.Metric("Codex", "Pro", [TestData.Window("Codex", "Session", 10, "1h 0m")]),
        ];

        var cards = TooltipCardBuilder.Build(new UsageSnapshot(results, [], []));

        Assert.Equal(["Codex", "Claude", "OpenRouter"], cards.Select(c => c.Title));
    }

    [Fact]
    public void Metric_card_carries_plan_and_metric_rows()
    {
        IReadOnlyList<ProviderResult> results =
        [
            ProviderResult.Metric("Codex", "Pro", [TestData.Window("Codex", "Session", 10, "1h 0m")]),
        ];

        var card = Assert.Single(TooltipCardBuilder.Build(new UsageSnapshot(results, [], [])));

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
        IReadOnlyList<ProviderResult> results = [ProviderResult.Balance("DeepSeek", "$9.99")];

        var card = Assert.Single(TooltipCardBuilder.Build(new UsageSnapshot(results, [], [])));

        Assert.Null(card.Plan);
        Assert.Empty(card.Metrics);
        Assert.Equal(["$9.99"], card.Lines);
    }
}
