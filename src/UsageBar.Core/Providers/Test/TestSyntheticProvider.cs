using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>Test provider for Synthetic — returns mock rolling-5h, weekly, and search-hourly windows.</summary>
public sealed class TestSyntheticProvider : IUsageProvider
{
    public ProviderDescriptor Descriptor { get; } = new("Synthetic", DisplayOrder: 15);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var rolling = TestData.RandomWindow("Synthetic", "Rolling 5h");
        var weekly = TestData.RandomWindow("Synthetic", "Weekly");
        var search = TestData.RandomWindow("Synthetic", "Search");
        return Task.FromResult<ProviderResult?>(
            new MetricResult("Synthetic", null, [rolling, weekly, search]));
    }
}
