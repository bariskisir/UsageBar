namespace UsageBar.Domain;

/// <summary>
/// A single rolling usage window reported by a metric provider.
/// </summary>
/// <param name="ProviderName">Owning provider, e.g. "Codex" or "Claude".</param>
/// <param name="Label">Window label shown to the user, e.g. "Session" or "Weekly".</param>
/// <param name="UsedPercent">Percentage of the window consumed, automatically clamped to 0-100.</param>
/// <param name="ResetText">Human-readable reset countdown (e.g. "2h 10m", "now"), or null if unknown.</param>
public sealed record UsageWindow
{
    public string ProviderName { get; init; }
    public string Label { get; init; }
    public double UsedPercent { get; init; }
    public string? ResetText { get; init; }

    public UsageWindow(string providerName, string label, double usedPercent, string? resetText = null)
    {
        ProviderName = providerName ?? throw new ArgumentNullException(nameof(providerName));
        Label = label ?? throw new ArgumentNullException(nameof(label));
        UsedPercent = double.IsFinite(usedPercent) ? Math.Clamp(usedPercent, 0, 100) : 0;
        ResetText = resetText;
    }
}
