using UsageBar.Domain;

namespace UsageBar.Application;

/// <summary>
/// Plan-aware selection of which usage windows become tray-icon bars, and in what order.
/// This is pure logic (no rendering) so the layout cases can be unit-tested; the Windows
/// shell turns the resulting <see cref="Bar"/> list into pixels.
/// </summary>
public static class IconLayout
{
    /// <summary>A single bar to render: a usage percentage (or null for an empty track) and its provider.</summary>
    public readonly record struct Bar(double? UsedPercent, string Provider);

    /// <summary>
    /// Computes the ordered bars. Mirrors the original CodexBar layout cases: Codex Pro +
    /// Claude subscriber → four bars; Codex Free → Weekly only; single provider → one or two
    /// bars; and sensible fallbacks. Returns a single empty bar when there is nothing to show.
    /// </summary>
    public static IReadOnlyList<Bar> Compute(IReadOnlyList<UsageWindow> windows, IReadOnlyList<ProviderPlan> plans)
    {
        var codex = windows.Where(w => w.ProviderName == "Codex").ToList();
        var claude = windows.Where(w => w.ProviderName == "Claude").ToList();

        var codexSession = FindWindow(codex, "Session");
        var codexWeekly = FindWindow(codex, "Weekly");
        var claudeSession = FindWindow(claude, "Session");
        var claudeWeekly = FindWindow(claude, "Weekly");

        var hasCodex = codex.Count > 0;
        var hasClaude = claude.Count > 0;

        var codexPlanIsFree = PlanIs(plans, "Codex", "Free");
        var codexFreeWindow = codexPlanIsFree ? codexWeekly ?? codexSession : codexWeekly;
        var codexIsFree = codexFreeWindow is not null && (codexPlanIsFree || codexSession is null);
        var codexIsPro = codexSession is not null && !codexPlanIsFree;
        var claudeIsSubscriber = claudeSession is not null && claudeWeekly is not null;

        var ordered = new List<Bar>();

        if (codexIsPro && hasClaude && claudeIsSubscriber)
        {
            // Codex Session+Weekly + Claude Session+Weekly → 25/25/25/25.
            ordered.Add(BarFor(codexSession!));
            ordered.Add(BarFor(codexWeekly!));
            ordered.Add(BarFor(claudeSession!));
            ordered.Add(BarFor(claudeWeekly!));
        }
        else if (codexIsFree && hasClaude && claudeIsSubscriber)
        {
            // Codex Free (Weekly only) + Claude Session+Weekly → 50/25/25.
            ordered.Add(BarFor(codexFreeWindow!));
            ordered.Add(BarFor(claudeSession!));
            ordered.Add(BarFor(claudeWeekly!));
        }
        else if (!hasCodex && claudeIsSubscriber)
        {
            // Claude only Session+Weekly → 50/50.
            ordered.Add(BarFor(claudeSession!));
            ordered.Add(BarFor(claudeWeekly!));
        }
        else if (codexIsPro && !hasClaude)
        {
            // Codex Pro Session(+Weekly) only.
            ordered.Add(BarFor(codexSession!));
            if (codexWeekly is not null)
            {
                ordered.Add(BarFor(codexWeekly));
            }
        }
        else if (codexIsFree && !hasClaude)
        {
            // Codex Free (Weekly only) → full bar.
            ordered.Add(BarFor(codexFreeWindow!));
        }
        else if (hasCodex && hasClaude)
        {
            // Mixed fallback: show whatever is present in a reasonable order.
            AddIfPresent(ordered, codexSession);
            AddIfPresent(ordered, codexWeekly);
            AddIfPresent(ordered, claudeSession);
            AddIfPresent(ordered, claudeWeekly);
        }
        else if (hasClaude)
        {
            // Claude only, not a full subscriber pair.
            AddIfPresent(ordered, claudeSession);
            AddIfPresent(ordered, claudeWeekly);
        }

        if (ordered.Count == 0)
        {
            ordered.Add(new Bar(UsedPercent: null, Provider: "None"));
        }

        return ordered;
    }

    private static Bar BarFor(UsageWindow window) => new(window.UsedPercent, window.ProviderName);

    private static void AddIfPresent(List<Bar> bars, UsageWindow? window)
    {
        if (window is not null)
        {
            bars.Add(BarFor(window));
        }
    }

    private static bool PlanIs(IReadOnlyList<ProviderPlan> plans, string providerName, string expected)
    {
        foreach (var plan in plans)
        {
            if (string.Equals(plan.ProviderName, providerName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(plan.Plan, expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static UsageWindow? FindWindow(List<UsageWindow> windows, string label)
    {
        foreach (var window in windows)
        {
            if (window.Label == label)
            {
                return window;
            }
        }

        return null;
    }
}
