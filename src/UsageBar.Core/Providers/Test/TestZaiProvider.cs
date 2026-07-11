using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>Test provider for Zai — returns a mock Token limit window with random usage.</summary>
public sealed class TestZaiProvider : ISingleResultUsageProvider
{
    public ProviderDescriptor Descriptor { get; } = new("Zai", DisplayOrder: 19);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var window = TestData.RandomWindow("Zai", "TOKENS_LIMIT");
        return Task.FromResult<ProviderResult?>(
            new MetricResult("Zai", null, [window]));
    }
}