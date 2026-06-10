using System.Globalization;

namespace UsageBar.Providers;

/// <summary>
/// Shared display formatting used across providers. Centralised here so balance and
/// metric providers stay consistent and free of duplicated logic.
/// </summary>
internal static class UsageFormatting
{
    /// <summary>
    /// Formats a currency amount as <c>{symbol}0.00</c> using the invariant culture.
    /// Defaults to the USD sign; pass another symbol (e.g. <c>"¥"</c>) for other currencies.
    /// </summary>
    public static string Currency(decimal value, string symbol = "$") =>
        string.Create(CultureInfo.InvariantCulture, $"{symbol}{value:0.00}");

    /// <summary>Capitalizes the first character of a plan/tier token for display (invariant).</summary>
    public static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    /// <summary>
    /// Formats a reset countdown as <c>now</c>, <c>5m</c>, <c>2h 10m</c>, or <c>1d 3h</c>.
    /// Returns <c>now</c> when the window has already reset.
    /// </summary>
    public static string ResetDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return "now";
        }

        if (duration >= TimeSpan.FromDays(1))
        {
            duration = TimeSpan.FromMinutes(Math.Ceiling(duration.TotalMinutes));
            return $"{(int)duration.TotalDays}d {duration.Hours}h";
        }

        var totalMinutes = Math.Max(1, (int)Math.Ceiling(duration.TotalMinutes));
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;

        return hours >= 1 ? $"{hours}h {minutes}m" : $"{minutes}m";
    }
}
