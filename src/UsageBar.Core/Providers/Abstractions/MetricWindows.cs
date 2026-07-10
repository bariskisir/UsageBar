using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>Window assembly helpers for metric providers.</summary>
internal static class MetricWindows
{
    public static IReadOnlyList<UsageWindow> Require(
        string providerName,
        params ReadOnlySpan<UsageWindow?> windows)
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
}
