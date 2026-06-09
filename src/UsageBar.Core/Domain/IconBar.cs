namespace UsageBar.Domain;

/// <summary>
/// A provider's contribution to the tray icon: one horizontal bar. <see cref="UsedPercent"/> is
/// <see langword="null"/> for an empty track. <see cref="Weight"/> is the bar's height relative to
/// the other bars (e.g. a Codex Free weekly bar uses weight 2 so it reads as a single full band
/// next to two weight-1 Claude bars → 50/25/25).
/// </summary>
public readonly record struct IconBar(double? UsedPercent, double Weight);
