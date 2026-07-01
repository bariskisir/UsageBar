using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>Test provider for Venice — returns a mock USD balance between $10.00 and $20.00.</summary>
public sealed class TestVeniceProvider : IUsageProvider
{
    public ProviderDescriptor Descriptor { get; } = new("Venice", DisplayOrder: 108);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken) =>
        Task.FromResult<ProviderResult?>(TestData.RandomBalance("Venice"));
}
