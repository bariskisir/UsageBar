using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>Test provider for Chutes — returns mock 4h Rolling and Monthly windows.</summary>
public sealed class TestChutesProvider : IUsageProvider
{
    public ProviderDescriptor Descriptor { get; } = new("Chutes", DisplayOrder: 17);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var fourHour = TestData.RandomWindow("Chutes", "4h Rolling");
        var monthly = TestData.RandomWindow("Chutes", "Monthly");
        return Task.FromResult<ProviderResult?>(
            new MetricResult("Chutes", null, [fourHour, monthly], TestData.Bars(fourHour, monthly)));
    }
}
