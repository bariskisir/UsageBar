using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>Test provider for MiniMax — randomly returns metric (model windows) or balance (points).</summary>
public sealed class TestMiniMaxProvider : IUsageProvider, IResultDisplayOrderProvider
{
    public ProviderDescriptor Descriptor { get; } = new("MiniMax", DisplayOrder: 25);

    public int GetDisplayOrder(ProviderResult result) => result switch
    {
        MetricResult => 25,
        BalanceResult => 130,
        _ => Descriptor.DisplayOrder,
    };

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var window = TestData.RandomWindow("MiniMax", "Model");
        var points = $"{Math.Round(1000 + Random.Shared.NextDouble() * 500, 2):0.00} pts";
        return Task.FromResult<ProviderResult?>(
            new MetricResult("MiniMax", points, [window]));
    }
}
