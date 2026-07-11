using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>Test provider for Claude — returns mock Session + Weekly windows with random 25–100% usage.</summary>
public sealed class TestClaudeProvider : ISingleResultUsageProvider
{
    private static readonly string[] Plans = ["Max", "Pro", "Team", "Enterprise", "Free", "Claude AI"];

    public ProviderDescriptor Descriptor { get; } = new("Claude", DisplayOrder: 10);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var (session, weekly) = TestData.RandomDualWindow("Claude");
        var plan = Plans[Random.Shared.Next(Plans.Length)];
        return Task.FromResult<ProviderResult?>(
            new MetricResult("Claude", plan, [session, weekly]));
    }
}