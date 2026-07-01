using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>Test provider for Copilot — returns mock Premium and Chat windows with random usage.</summary>
public sealed class TestCopilotProvider : IUsageProvider
{
    private static readonly string[] Plans = ["Free", "Individual", "Business", "Enterprise"];

    public ProviderDescriptor Descriptor { get; } = new("Copilot", DisplayOrder: 5);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var premium = TestData.RandomWindow("Copilot", "Premium");
        var chat = TestData.RandomWindow("Copilot", "Chat");
        var plan = Plans[Random.Shared.Next(Plans.Length)];
        return Task.FromResult<ProviderResult?>(
            new MetricResult("Copilot", plan, [premium, chat], TestData.Bars(premium, chat)));
    }
}
