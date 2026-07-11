using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>Test provider for OpenAI — returns a mock USD balance between $10.00 and $20.00.</summary>
public sealed class TestOpenAIProvider : ISingleResultUsageProvider
{
    public ProviderDescriptor Descriptor { get; } = new("OpenAI", DisplayOrder: 105);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken) =>
        Task.FromResult<ProviderResult?>(TestData.RandomBalance("OpenAI"));
}