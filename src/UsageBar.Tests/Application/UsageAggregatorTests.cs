using Microsoft.Extensions.Logging.Abstractions;
using UsageBar.Application;
using UsageBar.Domain;
using UsageBar.Providers;
using Xunit;

namespace UsageBar.Tests;

public sealed class UsageAggregatorTests
{
    [Fact]
    public async Task Merges_windows_and_skips_nulls()
    {
        ProviderResult metric = new MetricResult("Codex", "Pro", [TestData.Window("Codex", "Session", 10)], []);
        ProviderResult balance = new BalanceResult("DeepSeek", "$5.00");

        IReadOnlyList<IUsageProvider> providers =
        [
            new StubProvider("Codex", () => metric),
            new StubProvider("Skipped", () => null, 50),
            new StubProvider("DeepSeek", () => balance, 100),
        ];

        var snapshot = await UsageAggregator.RefreshAsync(providers, TestData.Context(), NullLogger.Instance, CancellationToken.None);

        Assert.Equal(2, snapshot.Results.Count);
        Assert.Single(snapshot.Windows);
    }

    [Fact]
    public async Task Orders_results_by_display_order()
    {
        ProviderResult codex = new MetricResult("Codex", "Pro", [], []);
        ProviderResult claude = new MetricResult("Claude", "Max", [], []);
        ProviderResult deepseek = new BalanceResult("DeepSeek", "$5.00");

        // Registered out of order; the aggregator must sort by display order.
        IReadOnlyList<IUsageProvider> providers =
        [
            new StubProvider("DeepSeek", () => deepseek, 100),
            new StubProvider("Claude", () => claude, 10),
            new StubProvider("Codex", () => codex, 0),
        ];

        var snapshot = await UsageAggregator.RefreshAsync(providers, TestData.Context(), NullLogger.Instance, CancellationToken.None);

        Assert.Equal(["Codex", "Claude", "DeepSeek"], snapshot.Results.Select(r => r.ProviderName));
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

        var snapshot = await UsageAggregator.RefreshAsync(providers, TestData.Context(), NullLogger.Instance, CancellationToken.None);

        var result = Assert.Single(snapshot.Results);
        Assert.Equal("DeepSeek", result.ProviderName);
    }
}
