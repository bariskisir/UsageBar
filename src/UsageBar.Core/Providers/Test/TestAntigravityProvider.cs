using UsageBar.Domain;

namespace UsageBar.Providers;

public sealed class TestAntigravityProvider : IUsageProvider
{
    public ProviderDescriptor Descriptor { get; } = new("Antigravity", DisplayOrder: 5);

    public Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var geminiSession = new UsageWindow(
            "Antigravity",
            "Session",
            RandomUsedPercent(),
            TestData.RandomWindow("Antigravity", "Session").ResetText,
            subLabel: "Gemini");

        var geminiWeekly = new UsageWindow(
            "Antigravity",
            "Weekly",
            RandomUsedPercent(),
            TestData.RandomWindow("Antigravity", "Weekly").ResetText,
            subLabel: "Gemini");

        var thirdPartySession = new UsageWindow(
            "Antigravity",
            "Session",
            RandomUsedPercent(),
            TestData.RandomWindow("Antigravity", "Session").ResetText,
            subLabel: "Claude and GPT");

        var thirdPartyWeekly = new UsageWindow(
            "Antigravity",
            "Weekly",
            RandomUsedPercent(),
            TestData.RandomWindow("Antigravity", "Weekly").ResetText,
            subLabel: "Claude and GPT");

        var windows = new List<UsageWindow> { geminiSession, geminiWeekly, thirdPartySession, thirdPartyWeekly };

        return Task.FromResult<ProviderResult?>(
            new MetricResult("Antigravity", "free-tier", windows, TestData.Bars(geminiSession, geminiWeekly)));
    }

    private static double RandomUsedPercent() =>
        Math.Round(2.0 + Random.Shared.NextDouble() * 38.0, 1);
}
