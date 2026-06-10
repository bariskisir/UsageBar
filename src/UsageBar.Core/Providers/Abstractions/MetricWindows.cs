using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>
/// Window/bar assembly helpers for metric providers. Providers keep full ownership of their bar
/// layout (count and weight); these cover the standard shapes so providers stay small.
/// </summary>
internal static class MetricWindows
{
    /// <summary>
    /// Collects the non-null windows in the given order, throwing a <see cref="ProviderException"/>
    /// when none are usable (the standard "response had no windows" failure).
    /// </summary>
    public static IReadOnlyList<UsageWindow> Require(string providerName, params ReadOnlySpan<UsageWindow?> windows)
    {
        var present = new List<UsageWindow>(windows.Length);
        foreach (var window in windows)
        {
            if (window is not null)
            {
                present.Add(window);
            }
        }

        if (present.Count == 0)
        {
            throw new ProviderException($"{providerName} response did not contain usable rate limit windows.");
        }

        return present;
    }

    /// <summary>
    /// One weight-1 bar per non-null window, in the given order — the standard metric-provider
    /// tray contribution.
    /// </summary>
    public static IReadOnlyList<IconBar> EqualWeightBars(params ReadOnlySpan<UsageWindow?> windows)
    {
        var bars = new List<IconBar>(windows.Length);
        foreach (var window in windows)
        {
            if (window is not null)
            {
                bars.Add(new IconBar(window.UsedPercent, 1.0));
            }
        }

        return bars;
    }
}
