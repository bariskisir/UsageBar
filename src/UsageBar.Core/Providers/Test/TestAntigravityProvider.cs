using UsageBar.Domain;

namespace UsageBar.Providers;

public sealed class TestAntigravityProvider : IUsageProvider
{
    public ProviderDescriptor Descriptor { get; } = new("Antigravity", DisplayOrder: 5);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var geminiWindow = new UsageWindow(
            "Antigravity",
            "Gemini - Weekly",
            RandomUsedPercent(),
            TestData.RandomWindow("Antigravity", "Weekly").ResetText);

        var thirdPartyWindow = new UsageWindow(
            "Antigravity",
            "Claude and GPT - Weekly",
            RandomUsedPercent(),
            TestData.RandomWindow("Antigravity", "Weekly").ResetText);

        var windows = new List<UsageWindow> { geminiWindow, thirdPartyWindow };

        return Task.FromResult<ProviderResult?>(
            new MetricResult("Antigravity", "free-tier", windows, TestData.Bars(geminiWindow, thirdPartyWindow)));
    }

    private static double RandomUsedPercent() =>
        Math.Round(2.0 + Random.Shared.NextDouble() * 38.0, 1);
}
