namespace UsageBar.Core.Domain;

/// <summary>
/// A single rolling usage window reported by a metric provider.
/// </summary>
/// <param name="ProviderName">Owning provider, e.g. "Codex" or "Claude".</param>
/// <param name="Label">Window label shown to the user, e.g. "Session" or "Weekly".</param>
/// <param name="UsedPercent">Percentage of the window consumed, automatically clamped to 0-100.</param>
/// <param name="ResetText">Human-readable reset countdown (e.g. "2h 10m", "now"), or null if unknown.</param>
/// <param name="SubLabel">Optional secondary label rendered in smaller font (e.g. model name in tooltip).</param>
/// <param name="ResetAt">Exact reset timestamp used by background window detection.</param>
public sealed record UsageWindow
{
    public string ProviderName { get; init; }
    public string Label { get; init; }
    public double UsedPercent { get; init; }
    public string? ResetText { get; init; }
    public string? SubLabel { get; init; }
    public DateTimeOffset? ResetAt { get; init; }

    public UsageWindow(
        string providerName,
        string label,
        double usedPercent,
        string? resetText = null,
        string? subLabel = null,
        DateTimeOffset? resetAt = null)
    {
        ProviderName = providerName ?? throw new ArgumentNullException(nameof(providerName));
        Label = label ?? throw new ArgumentNullException(nameof(label));
        UsedPercent = double.IsFinite(usedPercent) ? Math.Clamp(usedPercent, 0, 100) : 0;
        ResetText = resetText;
        SubLabel = subLabel;
        ResetAt = resetAt;
    }
}
