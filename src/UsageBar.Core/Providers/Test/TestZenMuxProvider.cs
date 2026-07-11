using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>Test provider for ZenMux - returns a mock USD balance.</summary>
public sealed class TestZenMuxProvider : ISingleResultUsageProvider
{
    public ProviderDescriptor Descriptor { get; } = new("ZenMux", DisplayOrder: 111);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken) =>
        Task.FromResult<ProviderResult?>(TestData.RandomBalance("ZenMux"));
}