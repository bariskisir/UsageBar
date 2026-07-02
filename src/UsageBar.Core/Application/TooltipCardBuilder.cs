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
    public static IReadOnlyList<TooltipCard> Build(UsageSnapshot snapshot)
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
                    cards.Add(new TooltipCard(balance.ProviderName, Plan: null, [], [balance.BalanceText]));
                    break;
            }
        }

        return cards;
    }
}
