using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>Test provider for DeepSeek — returns a mock USD balance between $10.00 and $20.00.</summary>
public sealed class TestDeepSeekProvider : IUsageProvider
{
    public ProviderDescriptor Descriptor { get; } = new("DeepSeek", DisplayOrder: 100);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken) =>
        Task.FromResult<ProviderResult?>(TestData.RandomBalance("DeepSeek"));
}
