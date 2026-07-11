using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>Test provider for Deepgram — returns a mock USD balance between $10.00 and $20.00.</summary>
public sealed class TestDeepgramProvider : ISingleResultUsageProvider
{
    public ProviderDescriptor Descriptor { get; } = new("Deepgram", DisplayOrder: 120);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken) =>
        Task.FromResult<ProviderResult?>(TestData.RandomBalance("Deepgram"));
}