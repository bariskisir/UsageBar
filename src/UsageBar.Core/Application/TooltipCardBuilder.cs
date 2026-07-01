using UsageBar.Domain;

namespace UsageBar.Application;

/// <summary>
/// Builds <see cref="TooltipCard"/>s from a <see cref="UsageSnapshot"/>: metric results become
/// metric cards (Session/Weekly bars + plan), balance results become balance cards. Results are
/// already ordered by the aggregator (metric providers sort before balance providers via their
/// display order), so cards are emitted in that order.
/// </summary>
internal static class TooltipCardBuilder
{
    public static IReadOnlyList<TooltipCard> Build(UsageSnapshot snapshot, double balanceHidingThreshold = -1)
    {
        var cards = new List<TooltipCard>(snapshot.Results.Count);

        foreach (var result in snapshot.Results)
        {
            switch (result)
            {
                case MetricResult metric when metric.Windows.Count > 0:
                    var metrics = metric.Windows
                        .Select(window => new TooltipMetric(window.Label, window.UsedPercent, window.ResetText ?? string.Empty))
                        .ToList();
                    cards.Add(new TooltipCard(metric.ProviderName, metric.Plan, metrics, []));
                    break;

                case BalanceResult balance:
                    var hide = ShouldHide(balance, balanceHidingThreshold);
                    cards.Add(new TooltipCard(balance.ProviderName, Plan: null, [], [balance.BalanceText], Hide: hide));
                    break;
            }
        }

        return cards;
    }

    /// <summary>
    /// Returns <see langword="true"/> when a balance card should be hidden based on the
    /// configured threshold. A threshold of -1 or NaN disables hiding. Negative balances
    /// are never hidden regardless of threshold (an overspent account is noteworthy).
    /// For providers with dual currencies (DeepSeek), both values must satisfy the
    /// threshold to hide.
    /// </summary>
    private static bool ShouldHide(BalanceResult balance, double threshold)
    {
        // Threshold of -1 (or NaN) means the feature is disabled.
        if (threshold < 0 || double.IsNaN(threshold))
        {
            return false;
        }

        var thresholdDecimal = (decimal)threshold;

        // For dual-currency providers: hide only when BOTH balances are at or below the threshold.
        if (balance.UsdAmount is { } usd && balance.CnyAmount is { } cny)
        {
            // Never hide negative balances — an overspent account is noteworthy.
            if (usd < 0 || cny < 0)
            {
                return false;
            }

            return usd <= thresholdDecimal && cny <= thresholdDecimal;
        }

        // For single-currency providers: hide when the USD amount is at or below the threshold.
        if (balance.UsdAmount is { } usdOnly)
        {
            if (usdOnly < 0)
            {
                return false;
            }

            return usdOnly <= thresholdDecimal;
        }

        // If no raw amounts are available (should not happen), show the card.
        return false;
    }
}
