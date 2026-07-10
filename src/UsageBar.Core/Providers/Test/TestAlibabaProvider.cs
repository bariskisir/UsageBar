using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>Test provider for Alibaba — returns mock 5h, Weekly, and Monthly windows.</summary>
public sealed class TestAlibabaProvider : IUsageProvider
{
    public ProviderDescriptor Descriptor { get; } = new("Alibaba", DisplayOrder: 23);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var hourly = TestData.RandomWindow("Alibaba", "5h");
        var weekly = TestData.RandomWindow("Alibaba", "Weekly");
        var monthly = TestData.RandomWindow("Alibaba", "Monthly");
        return Task.FromResult<ProviderResult?>(
            new MetricResult("Alibaba", "Coding Plan", [hourly, weekly, monthly]));
    }
}
