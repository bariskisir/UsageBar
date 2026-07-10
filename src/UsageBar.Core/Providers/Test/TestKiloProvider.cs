using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>
/// Test provider for Kilo — randomly returns a metric card (plan mode), a balance card,
/// or both, so every test refresh shows a different combination.
/// Implements <see cref="IResultDisplayOrderProvider"/> so the aggregator places the Pass bar
/// after Claude (metric order 15) like the real Kilo provider does.
/// </summary>
public sealed class TestKiloProvider : IUsageProvider, IResultDisplayOrderProvider
{
    private static readonly string[] Plans = ["Starter", "Pro", "Expert", "Kilo Pass"];

    public ProviderDescriptor Descriptor { get; } = new("Kilo", DisplayOrder: 30);

    public int GetDisplayOrder(ProviderResult result) => result switch
    {
        MetricResult => 15,
        BalanceResult => 116,
        _ => Descriptor.DisplayOrder,
    };

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var pass = TestData.RandomWindow("Kilo", "Pass");
        var plan = Plans[Random.Shared.Next(Plans.Length)];
        return Task.FromResult<ProviderResult?>(
            new MetricResult("Kilo", plan, [pass]));
    }

    public Task<IReadOnlyList<ProviderResult>> GetUsageResultsAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var results = new List<ProviderResult>();
        var mode = Random.Shared.Next(3);

        switch (mode)
        {
            // 1 — plan mode (MetricResult with Pass window, no balance)
            case 0:
            {
                var pass = TestData.RandomWindow("Kilo", "Pass");
                var plan = Plans[Random.Shared.Next(Plans.Length)];
                results.Add(new MetricResult("Kilo", plan, [pass]));
                break;
            }
            // 2 — balance mode (BalanceResult only)
            case 1:
            {
                results.Add(TestData.RandomBalance("Kilo"));
                break;
            }
            // 3 — both: MetricResult with plan + balance in the plan line
            default:
            {
                var pass = TestData.RandomWindow("Kilo", "Pass");
                var plan = Plans[Random.Shared.Next(Plans.Length)];
                var balance = TestData.RandomBalance("Kilo");
                var planLine = $"{plan} - {balance.BalanceText}";
                results.Add(new MetricResult("Kilo", planLine, [pass]));
                break;
            }
        }

        return Task.FromResult<IReadOnlyList<ProviderResult>>(results);
    }
}
