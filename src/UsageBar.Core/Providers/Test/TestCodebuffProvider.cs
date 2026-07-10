using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>Test provider for Codebuff — returns a mock Quota window with random 25–100% usage and a balance plan line.</summary>
public sealed class TestCodebuffProvider : IUsageProvider
{
    public ProviderDescriptor Descriptor { get; } = new("Codebuff", DisplayOrder: 27);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var window = TestData.RandomWindow("Codebuff", "Quota");
        var balance = UsageFormatting.Currency(Math.Round(10m + (decimal)Random.Shared.NextDouble() * 10m, 2));
        return Task.FromResult<ProviderResult?>(
            new MetricResult("Codebuff", balance, [window]));
    }
}
