using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>Test provider for Warp — returns a mock Requests window with random usage.</summary>
public sealed class TestWarpProvider : IUsageProvider
{
    public ProviderDescriptor Descriptor { get; } = new("Warp", DisplayOrder: 13);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var window = TestData.RandomWindow("Warp", "Requests");
        return Task.FromResult<ProviderResult?>(
            new MetricResult("Warp", null, [window]));
    }
}
