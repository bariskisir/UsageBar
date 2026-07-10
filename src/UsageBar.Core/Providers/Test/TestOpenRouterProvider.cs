using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>Test provider for OpenRouter — returns a mock USD balance between $10.00 and $20.00.</summary>
public sealed class TestOpenRouterProvider : IUsageProvider
{
    public ProviderDescriptor Descriptor { get; } = new("OpenRouter", DisplayOrder: 110);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken) =>
        Task.FromResult<ProviderResult?>(TestData.RandomBalance("OpenRouter"));
}
