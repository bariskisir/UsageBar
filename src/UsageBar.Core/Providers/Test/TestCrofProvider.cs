using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>Test provider for Crof — returns a mock USD balance between $10.00 and $20.00.</summary>
public sealed class TestCrofProvider : IUsageProvider
{
    public ProviderDescriptor Descriptor { get; } = new("Crof", DisplayOrder: 112);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken) =>
        Task.FromResult<ProviderResult?>(TestData.RandomBalance("Crof"));
}
