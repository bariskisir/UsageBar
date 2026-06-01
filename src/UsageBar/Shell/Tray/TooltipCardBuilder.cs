using UsageBar.Application;
using UsageBar.Domain;

namespace UsageBar.Shell.Tray;

/// <summary>
/// Builds <see cref="TooltipCard"/> instances from a <see cref="UsageSnapshot"/>,
/// mirroring Rust's <c>tooltip::build_cards</c>.
/// </summary>
internal static class TooltipCardBuilder
{
    public static IReadOnlyList<TooltipCard> Build(UsageSnapshot snapshot)
    {
        var cards = new List<TooltipCard>();

        // Group windows by provider name for metric cards.
        var windowGroups = snapshot.Windows
            .GroupBy(w => w.ProviderName)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Extract reset detail from usage blocks (e.g. "42.3%, 2h 10m" → "2h 10m").
        var blockDetail = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in snapshot.Blocks)
        {
            if (!block.Inline)
            {
                continue;
            }

            // Label is like "Codex 5h:" or "Claude 7d:"
            var key = block.Label.TrimEnd(':', ' ');
            var comma = block.Value.IndexOf(',');
            if (comma >= 0)
            {
                blockDetail[key] = block.Value[(comma + 1)..].Trim();
            }
        }

        // Build metric cards from windows.
        var planMap = snapshot.Plans
            .GroupBy(p => p.Provider)
            .ToDictionary(g => g.Key, g => g.First().Plan, StringComparer.OrdinalIgnoreCase);

        var metricCards = new List<TooltipCard>();
        foreach (var (providerName, windows) in windowGroups)
        {
            var plan = planMap.GetValueOrDefault(providerName);
            var metrics = new List<TooltipMetric>();

            foreach (var w in windows)
            {
                var detailKey = $"{w.ProviderName} {w.WindowLabel}";
                var detail = blockDetail.GetValueOrDefault(detailKey) ?? string.Empty;
                metrics.Add(new TooltipMetric(w.WindowLabel, w.UsedPercent, detail));
            }

            metricCards.Add(new TooltipCard(providerName, plan, metrics, []));
        }

        // Build balance cards from blocks where the provider does NOT have
        // rate-limit windows (DeepSeek, OpenRouter, Deepgram).
        // Block labels are like "Claude Session:" or "OpenRouter:" — extract the
        // first word as the provider name and skip blocks from metric providers.
        var balanceCards = new List<TooltipCard>();
        foreach (var block in snapshot.Blocks)
        {
            var label = block.Label.TrimEnd(':', ' ');
            var firstWord = label.Split(' ')[0];

            // Skip blocks whose provider already has a metric card.
            if (windowGroups.ContainsKey(firstWord))
            {
                continue;
            }

            balanceCards.Add(new TooltipCard(
                label,
                null,
                [],
                [block.Value]));
        }

        // Sort: metric cards before balance cards; within each group, Codex first.
        metricCards.Sort(CardOrder);
        balanceCards.Sort(CardOrder);

        cards.AddRange(metricCards);
        cards.AddRange(balanceCards);
        return cards;
    }

    private static int CardOrder(TooltipCard a, TooltipCard b)
    {
        // Metric cards before balance cards.
        var aIsMetric = a.Metrics.Count > 0;
        var bIsMetric = b.Metrics.Count > 0;
        if (aIsMetric != bIsMetric)
        {
            return aIsMetric ? -1 : 1;
        }

        return ProviderRank(a.Title).CompareTo(ProviderRank(b.Title));
    }

    private static int ProviderRank(string name)
    {
        return name switch
        {
            "Codex" => 0,
            "Claude" => 1,
            _ => 2
        };
    }
}
