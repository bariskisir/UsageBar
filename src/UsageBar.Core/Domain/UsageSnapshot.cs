namespace UsageBar.Domain;

/// <summary>
/// Aggregated results of one refresh across all configured providers.
/// </summary>
/// <param name="Results">Per-provider results (only providers that returned data).</param>
/// <param name="Windows">All metric windows, flattened, for icon rendering and threshold checks.</param>
/// <param name="Plans">Provider plan labels, where reported.</param>
public sealed record UsageSnapshot(
    IReadOnlyList<ProviderResult> Results,
    IReadOnlyList<UsageWindow> Windows,
    IReadOnlyList<ProviderPlan> Plans)
{
    /// <summary>An empty snapshot (no configured providers / no data).</summary>
    public static UsageSnapshot Empty { get; } = new([], [], []);
}
