using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>Test provider for Codex — returns mock Session + Weekly windows with random 25–100% usage.</summary>
public sealed class TestCodexProvider : IUsageProvider
{
    private static readonly string[] Plans = ["Free", "Plus", "Pro", "Pro Lite", "Go", "Team", "Business", "Enterprise", "Education", "Guest"];

    public ProviderDescriptor Descriptor { get; } = new("Codex", DisplayOrder: 0);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var (session, weekly) = TestData.RandomDualWindow("Codex");
        var plan = Plans[Random.Shared.Next(Plans.Length)];
        return Task.FromResult<ProviderResult?>(
            new MetricResult("Codex", plan, [session, weekly], TestData.Bars(session, weekly)));
    }
}
