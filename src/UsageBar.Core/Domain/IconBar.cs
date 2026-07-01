using System.Diagnostics.CodeAnalysis;

namespace UsageBar.Domain;

/// <summary>
/// A provider's contribution to the tray icon: one horizontal bar. <see cref="UsedPercent"/>
/// is automatically clamped to 0–100; <see langword="null"/> means an empty track.
/// <see cref="Weight"/> is the bar's height relative to the other bars and is forced to a
/// minimum of 0.001 to avoid division-by-zero downstream.
/// </summary>
public readonly record struct IconBar(double? UsedPercent, double Weight)
{
    /// <summary>Clamped+guarded init; always prefer this over the auto-generated constructor.</summary>
    public static IconBar Create(double? usedPercent, double weight) =>
        new(usedPercent.HasValue && double.IsFinite(usedPercent.Value) ? Math.Clamp(usedPercent.Value, 0, 100) : null, Math.Max(0.001, weight));
}
