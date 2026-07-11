using Microsoft.Extensions.Logging.Abstractions;
using UsageBar.Core.Application;
using UsageBar.Core.Domain;
using UsageBar.Core.Providers;
using Xunit;

namespace UsageBar.Tests;

public sealed class UsageAggregatorTests
{
    private static UsageAggregator Aggregator() =>
        new(UsageRefreshOptions.Default, NullLogger<UsageAggregator>.Instance);

    [Fact]
    public async Task Merges_windows_and_skips_nulls()
    {
        ProviderResult metric = new MetricResult("Codex", "Pro", [TestData.Window("Codex", "Session", 10)]);
        ProviderResult balance = new BalanceResult("DeepSeek", "$5.00");

        IReadOnlyList<IUsageProvider> providers =
        [
            new StubProvider("Codex", () => metric),
            new StubProvider("Skipped", () => null, 50),
            new StubProvider("DeepSeek", () => balance, 100),
        ];

        var snapshot = await Aggregator().RefreshAsync(providers, TestData.Context(), CancellationToken.None);

        Assert.Equal(2, snapshot.Results.Count);
        Assert.Single(snapshot.Windows);
    }

    [Fact]
    public async Task Orders_results_by_display_order()
    {
        ProviderResult codex = new MetricResult("Codex", "Pro", []);
        ProviderResult claude = new MetricResult("Claude", "Max", []);
        ProviderResult deepseek = new BalanceResult("DeepSeek", "$5.00");

        // Registered out of order; the aggregator must sort by display order.
        IReadOnlyList<IUsageProvider> providers =
        [
            new StubProvider("DeepSeek", () => deepseek, 100),
            new StubProvider("Claude", () => claude, 10),
            new StubProvider("Codex", () => codex, 0),
        ];

        var snapshot = await Aggregator().RefreshAsync(providers, TestData.Context(), CancellationToken.None);

        Assert.Equal(["Codex", "Claude", "DeepSeek"], snapshot.Results.Select(r => r.ProviderName));
    }

    [Fact]
    public async Task Allows_provider_to_order_by_result_kind()
    {
        ProviderResult claude = new MetricResult("Claude", "Max", [TestData.Window("Claude", "Session", 10)]);
        ProviderResult elevenLabs = new MetricResult("ElevenLabs", "Pro", [TestData.Window("ElevenLabs", "Session", 20)]);
        ProviderResult kimi = new BalanceResult("Moonshot (Kimi)", "$5.00");
        ProviderResult kiloMetric = new MetricResult("Kilo", "Pro", [TestData.Window("Kilo", "Pass", 30)]);
        ProviderResult kiloBalance = new BalanceResult("Kilo", "$12.50");

        var metricSnapshot = await Aggregator().RefreshAsync(
            [
                new StubProvider("Claude", () => claude, 10),
                new StubProvider("ElevenLabs", () => elevenLabs, 20),
                new DynamicOrderProvider("Kilo", () => kiloMetric, metricOrder: 15, balanceOrder: 116),
                new StubProvider("Moonshot (Kimi)", () => kimi, 115),
            ],
            TestData.Context(),
            CancellationToken.None);

        Assert.Equal(["Claude", "Kilo", "ElevenLabs", "Moonshot (Kimi)"], metricSnapshot.Results.Select(r => r.ProviderName));
        Assert.Equal(["Claude", "Kilo", "ElevenLabs"], metricSnapshot.Windows.Select(w => w.ProviderName));

        var balanceSnapshot = await Aggregator().RefreshAsync(
            [
                new StubProvider("Claude", () => claude, 10),
                new StubProvider("ElevenLabs", () => elevenLabs, 20),
                new StubProvider("Moonshot (Kimi)", () => kimi, 115),
                new DynamicOrderProvider("Kilo", () => kiloBalance, metricOrder: 15, balanceOrder: 116),
            ],
            TestData.Context(),
            CancellationToken.None);

        Assert.Equal(["Claude", "ElevenLabs", "Moonshot (Kimi)", "Kilo"], balanceSnapshot.Results.Select(r => r.ProviderName));
    }

    [Fact]
    public async Task Isolates_a_throwing_provider()
    {
        ProviderResult good = new BalanceResult("DeepSeek", "$5.00");
        IReadOnlyList<IUsageProvider> providers =
        [
            new StubProvider("Broken", () => throw new ProviderException("boom")),
            new StubProvider("DeepSeek", () => good, 100),
        ];

        var snapshot = await Aggregator().RefreshAsync(providers, TestData.Context(), CancellationToken.None);

        var result = Assert.Single(snapshot.Results);
        Assert.Equal("DeepSeek", result.ProviderName);
    }
}