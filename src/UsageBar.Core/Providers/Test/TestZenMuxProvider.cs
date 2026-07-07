using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>Test provider for ZenMux - returns a mock USD balance.</summary>
public sealed class TestZenMuxProvider : IUsageProvider
{
    public ProviderDescriptor Descriptor { get; } = new("ZenMux", DisplayOrder: 111);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken) =>
        Task.FromResult<ProviderResult?>(TestData.RandomBalance("ZenMux"));
}
