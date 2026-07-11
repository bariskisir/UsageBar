using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>Test provider for Moonshot (Kimi) — returns a mock USD balance between $10.00 and $20.00.</summary>
public sealed class TestMoonshotProvider : ISingleResultUsageProvider
{
    public ProviderDescriptor Descriptor { get; } = new(@"Moonshot (Kimi)", DisplayOrder: 115);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken) =>
        Task.FromResult<ProviderResult?>(TestData.RandomBalance(@"Moonshot (Kimi)"));
}