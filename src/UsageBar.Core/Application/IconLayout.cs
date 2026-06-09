using UsageBar.Domain;

namespace UsageBar.Application;

/// <summary>
/// Flattens the per-provider tray bars from a refresh into the final ordered list the shell
/// rasterizes. Each metric provider decides its own bars (count + weight) in its result, so this
/// is pure ordering/concatenation — provider-agnostic and unit-testable.
/// </summary>
public static class IconLayout
{
    /// <summary>
    /// A single bar to render: a usage percentage (or null for an empty track), its height weight
    /// relative to the other bars, and the owning provider (used for inter-bar spacing).
    /// </summary>
    public readonly record struct Bar(double? UsedPercent, double Weight, string Provider);

    /// <summary>
    /// Concatenates each metric result's <see cref="MetricResult.IconBars"/> in the order the
    /// results are given (the aggregator pre-sorts by display order). Returns a single empty bar
    /// when there is nothing to show.
    /// </summary>
    public static IReadOnlyList<Bar> Compute(IReadOnlyList<ProviderResult> results)
    {
        var bars = new List<Bar>();

        foreach (var result in results)
        {
            if (result is not MetricResult metric)
            {
                continue;
            }

            foreach (var bar in metric.IconBars)
            {
                bars.Add(new Bar(bar.UsedPercent, bar.Weight, metric.ProviderName));
            }
        }

        if (bars.Count == 0)
        {
            bars.Add(new Bar(UsedPercent: null, Weight: 1.0, Provider: "None"));
        }

        return bars;
    }
}
