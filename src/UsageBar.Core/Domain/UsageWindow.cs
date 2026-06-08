namespace UsageBar.Domain;

/// <summary>
/// A single rolling usage window reported by a metric provider.
/// </summary>
/// <param name="ProviderName">Owning provider, e.g. "Codex" or "Claude".</param>
/// <param name="Label">Window label shown to the user, e.g. "Session" or "Weekly".</param>
/// <param name="UsedPercent">Percentage of the window consumed, clamped to 0-100.</param>
/// <param name="ResetText">Human-readable reset countdown (e.g. "2h 10m", "now"), or null if unknown.</param>
public sealed record UsageWindow(
    string ProviderName,
    string Label,
    double UsedPercent,
    string? ResetText = null);
