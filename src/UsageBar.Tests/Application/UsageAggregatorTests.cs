using Microsoft.Extensions.Logging.Abstractions;
using UsageBar.Application;
using UsageBar.Domain;
using UsageBar.Providers;
using Xunit;

namespace UsageBar.Tests;

public sealed class UsageAggregatorTests
{
    [Fact]
    public async Task Merges_windows_and_plans_and_skips_nulls()
    {
        var metric = ProviderResult.Metric("Codex", "Pro", [TestData.Window("Codex", "Session", 10)]);
        var balance = ProviderResult.Balance("DeepSeek", "$5.00");

        IReadOnlyList<IUsageProvider> providers =
        [
            new StubProvider("Codex", ProviderCategory.Metric, () => metric),
            new StubProvider("Skipped", ProviderCategory.Balance, () => null),
            new StubProvider("DeepSeek", ProviderCategory.Balance, () => balance),
        ];

        var snapshot = await UsageAggregator.RefreshAsync(providers, TestData.Context(), NullLogger.Instance, CancellationToken.None);

        Assert.Equal(2, snapshot.Results.Count);
        Assert.Single(snapshot.Windows);
        var plan = Assert.Single(snapshot.Plans);
        Assert.Equal("Codex", plan.ProviderName);
        Assert.Equal("Pro", plan.Plan);
    }

    [Fact]
    public async Task Isolates_a_throwing_provider()
    {
        var good = ProviderResult.Balance("DeepSeek", "$5.00");
        IReadOnlyList<IUsageProvider> providers =
        [
            new StubProvider("Broken", ProviderCategory.Balance, () => throw new ProviderException("boom")),
            new StubProvider("DeepSeek", ProviderCategory.Balance, () => good),
        ];

        var snapshot = await UsageAggregator.RefreshAsync(providers, TestData.Context(), NullLogger.Instance, CancellationToken.None);

        var result = Assert.Single(snapshot.Results);
        Assert.Equal("DeepSeek", result.ProviderName);
    }
}
