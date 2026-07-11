using UsageBar.Core.Domain;

namespace UsageBar.Core.Application;

/// <summary>
/// Builds <see cref="TooltipCard"/>s from a <see cref="UsageSnapshot"/>: metric results become
/// metric cards (Session/Weekly bars + plan), balance results become balance cards. Results are
/// already ordered by the aggregator (metric providers sort before balance providers via their
/// display order), so cards are emitted in that order.
/// </summary>
internal static class TooltipCardBuilder
{
    public static IReadOnlyList<TooltipCard> Build(
        UsageSnapshot snapshot,
        IReadOnlyDictionary<string, string?>? iconKeys = null)
    {
        var cards = new List<TooltipCard>(snapshot.Results.Count);

        foreach (var result in snapshot.Results)
        {
            switch (result)
            {
                case MetricResult metric when metric.Windows.Count > 0:
                    var metrics = metric.Windows
                        .Select(window => new TooltipMetric(window.Label, window.UsedPercent, window.ResetText ?? string.Empty, window.SubLabel))
                        .ToList();
                    cards.Add(new TooltipCard(
                        metric.ProviderName,
                        metric.Plan,
                        metrics,
                        [],
                        IconKey: IconKeyFor(metric.ProviderName, iconKeys)));
                    break;

                case BalanceResult balance:
                    cards.Add(new TooltipCard(
                        balance.ProviderName,
                        Plan: null,
                        [],
                        [balance.BalanceText],
                        IconKey: IconKeyFor(balance.ProviderName, iconKeys)));
                    break;
            }
        }

        return cards;
    }

    private static string? IconKeyFor(string providerName, IReadOnlyDictionary<string, string?>? iconKeys) =>
        iconKeys is not null && iconKeys.TryGetValue(providerName, out var iconKey) ? iconKey : null;
}