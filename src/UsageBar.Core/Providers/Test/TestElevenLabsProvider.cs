using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>Test provider for ElevenLabs — returns a mock Session window with random 25–100% usage.</summary>
public sealed class TestElevenLabsProvider : ISingleResultUsageProvider
{
    private static readonly string[] Plans = ["Free", "Starter", "Creator", "Pro", "Scale", "Business", "Enterprise"];

    public ProviderDescriptor Descriptor { get; } = new("ElevenLabs", DisplayOrder: 20);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var session = TestData.RandomWindow("ElevenLabs", "Session");
        var plan = Plans[Random.Shared.Next(Plans.Length)];
        return Task.FromResult<ProviderResult?>(
            new MetricResult("ElevenLabs", plan, [session]));
    }
}