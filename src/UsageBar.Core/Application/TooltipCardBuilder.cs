using UsageBar.Domain;

namespace UsageBar.Application;

/// <summary>
/// Builds <see cref="TooltipCard"/>s from a <see cref="UsageSnapshot"/>: metric providers
/// become metric cards (Session/Weekly bars + plan), balance providers become balance
/// cards. Metric cards sort before balance cards; Codex sorts before Claude.
/// </summary>
internal static class TooltipCardBuilder
{
    public static IReadOnlyList<TooltipCard> Build(UsageSnapshot snapshot)
    {
        var metricCards = new List<TooltipCard>();
        var balanceCards = new List<TooltipCard>();

        foreach (var result in snapshot.Results)
        {
            if (result.Category == ProviderCategory.Metric)
            {
                if (result.Windows.Count == 0)
                {
                    continue;
                }

                var metrics = result.Windows
                    .Select(window => new TooltipMetric(window.Label, window.UsedPercent, window.ResetText ?? string.Empty))
                    .ToList();

                metricCards.Add(new TooltipCard(result.ProviderName, result.Plan, metrics, []));
            }
            else
            {
                balanceCards.Add(new TooltipCard(result.ProviderName, Plan: null, [], [result.BalanceText ?? string.Empty]));
            }
        }

        metricCards.Sort(static (a, b) => ProviderRank(a.Title).CompareTo(ProviderRank(b.Title)));
        balanceCards.Sort(static (a, b) => ProviderRank(a.Title).CompareTo(ProviderRank(b.Title)));

        var cards = new List<TooltipCard>(metricCards.Count + balanceCards.Count);
        cards.AddRange(metricCards);
        cards.AddRange(balanceCards);
        return cards;
    }

    private static int ProviderRank(string name) => name switch
    {
        "Codex" => 0,
        "Claude" => 1,
        _ => 2,
    };
}
