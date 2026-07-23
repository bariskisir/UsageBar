using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

public sealed class TestCommandCodeProvider : ISingleResultUsageProvider
{
    private static readonly string[] Plans = ["Go", "Pro", "Team", "Enterprise", "Free"];

    public ProviderDescriptor Descriptor { get; } = new("Command Code", DisplayOrder: 6);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var (session, weekly) = TestData.RandomDualWindow("CommandCode");
        var plan = Plans[Random.Shared.Next(Plans.Length)];
        return Task.FromResult<ProviderResult?>(
            new MetricResult("CommandCode", plan, [session, weekly]));
    }
}
