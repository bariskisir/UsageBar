namespace UsageBar.Domain;

/// <summary>
/// Aggregated results of one refresh across all configured providers, ordered by provider
/// display order (see <c>ProviderDescriptor.DisplayOrder</c>) so downstream icon bars and
/// tooltip cards come out in a stable, provider-defined order.
/// </summary>
/// <param name="Results">Per-provider results (only providers that returned data), in display order.</param>
/// <param name="Windows">All metric windows, flattened, for threshold checks.</param>
public sealed record UsageSnapshot(
    IReadOnlyList<ProviderResult> Results,
    IReadOnlyList<UsageWindow> Windows);
