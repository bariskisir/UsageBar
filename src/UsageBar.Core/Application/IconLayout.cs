using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;

namespace UsageBar.Core.Application;

/// <summary>
/// Builds the final ordered tray-icon bars from metric windows. Default mode shows all metric
/// windows equally in provider display order; manual mode uses user-configured window keys and
/// weights.
/// </summary>
public static class IconLayout
{
    /// <summary>
    /// A single bar to render: a usage percentage (or null for an empty track), its height weight
    /// relative to the other bars, and the owning provider (used for inter-bar spacing).
    /// </summary>
    public readonly record struct Bar(double? UsedPercent, double Weight, string Provider);

    /// <summary>
    /// Builds bars from metric windows in default mode. Kept as the simple default for tests and
    /// callers that do not have settings.
    /// </summary>
    public static IReadOnlyList<Bar> Compute(IReadOnlyList<ProviderResult> results) =>
        Compute(results, TrayIconLayoutSettings.Default);

    /// <summary>
    /// Builds bars from metric windows using the configured icon layout. Returns a single empty
    /// bar when there is nothing to show.
    /// </summary>
    public static IReadOnlyList<Bar> Compute(IReadOnlyList<ProviderResult> results, TrayIconLayoutSettings? settings)
    {
        var normalized = (settings ?? TrayIconLayoutSettings.Default).Normalize();
        var bars = normalized.IsManual
            ? ManualBars(results, normalized)
            : DefaultBars(results);

        if (bars.Count == 0)
        {
            bars.Add(new Bar(UsedPercent: null, Weight: 1.0, Provider: "None"));
        }

        return bars;
    }

    public static string WindowKey(string providerName, string label) => NormalizeKey($"{providerName}_{label}");

    private static List<Bar> DefaultBars(IReadOnlyList<ProviderResult> results)
    {
        var bars = new List<Bar>();

        foreach (var result in results)
        {
            if (result is not MetricResult metric)
            {
                continue;
            }

            foreach (var window in metric.Windows)
            {
                bars.Add(new Bar(window.UsedPercent, Weight: 1.0, metric.ProviderName));
            }
        }

        return bars;
    }

    private static List<Bar> ManualBars(IReadOnlyList<ProviderResult> results, TrayIconLayoutSettings settings)
    {
        var windowsByKey = new Dictionary<string, UsageWindow>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in results)
        {
            if (result is not MetricResult metric)
            {
                continue;
            }

            foreach (var window in metric.Windows)
            {
                windowsByKey[WindowKey(window.ProviderName, window.Label)] = window;
            }
        }

        var bars = new List<Bar>();
        var totalWeight = 0.0;
        foreach (var (rawKey, weight) in settings.Bars ?? [])
        {
            var isWildcard = rawKey.Contains('*');
            var normalizedKey = NormalizeKey(rawKey);

            if (!isWildcard && windowsByKey.TryGetValue(normalizedKey, out var window))
            {
                bars.Add(new Bar(window.UsedPercent, weight, window.ProviderName));
                totalWeight += weight;
                continue;
            }

            if (isWildcard)
            {
                // Wildcard: match all windows whose key starts with the normalized prefix + "_".
                // e.g. "minimax_*" → normalized "minimax" → prefix "minimax_" matches "minimax_abab6_5s".
                var prefix = string.IsNullOrEmpty(normalizedKey) ? "_" : normalizedKey + "_";
                var matched = false;
                foreach (var (windowKey, matchedWindow) in windowsByKey)
                {
                    if (windowKey.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        bars.Add(new Bar(matchedWindow.UsedPercent, weight, matchedWindow.ProviderName));
                        totalWeight += weight;
                        matched = true;
                    }
                }

                if (matched)
                {
                    continue;
                }
            }

            // Fallback: try exact match on normalized key (handles keys that happen to
            // contain '*' as a literal character after normalization).
            if (isWildcard && windowsByKey.TryGetValue(normalizedKey, out var fallbackWindow))
            {
                bars.Add(new Bar(fallbackWindow.UsedPercent, weight, fallbackWindow.ProviderName));
                totalWeight += weight;
            }
        }

        if (bars.Count > 0 && totalWeight < 100.0)
        {
            bars.Add(new Bar(UsedPercent: null, Weight: 100.0 - totalWeight, Provider: "None"));
        }

        return bars;
    }

    private static string NormalizeKey(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        var lastWasSeparator = false;

        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                buffer[length++] = char.ToLowerInvariant(c);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator && length > 0)
            {
                buffer[length++] = '_';
                lastWasSeparator = true;
            }
        }

        if (length > 0 && buffer[length - 1] == '_')
        {
            length--;
        }

        return new string(buffer[..length]);
    }
}
