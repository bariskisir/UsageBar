using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>Test provider for Poe — returns a mock point balance.</summary>
public sealed class TestPoeProvider : IUsageProvider
{
    public ProviderDescriptor Descriptor { get; } = new("Poe", DisplayOrder: 118);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var points = Math.Round(100 + Random.Shared.NextDouble() * 500, 2);
        var text = $"{points:0.00} pts";
        return Task.FromResult<ProviderResult?>(new BalanceResult("Poe", text));
    }
}
