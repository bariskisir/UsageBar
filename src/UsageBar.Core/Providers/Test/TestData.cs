using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>
/// Shared mock-data helpers for test providers. Each call to a generator produces fresh
/// random values so the tooltip and tray icon change on every refresh.
/// </summary>
internal static class TestData
{
    public static (UsageWindow Session, UsageWindow Weekly) RandomDualWindow(string providerName)
    {
        var session = RandomWindow(providerName, "Session");
        var weekly = RandomWindow(providerName, "Weekly");
        return (session, weekly);
    }

    public static UsageWindow RandomWindow(string providerName, string label, string? subLabel = null)
    {
        var percent = Math.Round(25.0 + Random.Shared.NextDouble() * 75.0, 1);
        return new UsageWindow(providerName, label, percent, RandomResetText(), subLabel);
    }

    public static IReadOnlyList<IconBar> Bars(UsageWindow w1, UsageWindow? w2 = null)
    {
        var bars = new List<IconBar> { IconBar.Create(w1.UsedPercent, 1.0) };
        if (w2 is not null)
        {
            bars.Add(IconBar.Create(w2.UsedPercent, 1.0));
        }

        return bars;
    }

    public static BalanceResult RandomBalance(string providerName)
    {
        var amount = Math.Round(10m + (decimal)Random.Shared.NextDouble() * 10m, 2);
        return new BalanceResult(providerName, UsageFormatting.Currency(amount), amount);
    }

    private static string RandomResetText()
    {
        return Random.Shared.Next(3) switch
        {
            0 => $"{Random.Shared.Next(1, 60)}m",
            1 => $"{Random.Shared.Next(1, 24)}h {Random.Shared.Next(0, 60)}m",
            2 => $"{Random.Shared.Next(1, 7)}d {Random.Shared.Next(0, 24)}h",
            _ => $"{Random.Shared.Next(1, 60)}m",
        };
    }
}
