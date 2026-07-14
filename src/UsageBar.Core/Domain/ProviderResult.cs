namespace UsageBar.Core.Domain;

/// <summary>
/// The outcome of a single provider refresh. A provider returns exactly one concrete kind —
/// <see cref="MetricResult"/> or <see cref="BalanceResult"/> — never a value that conflates both;
/// consumers pattern-match on the concrete type.
/// </summary>
public abstract record ProviderResult(string ProviderName);

/// <summary>
/// A metric provider's result: usage windows and an optional plan/tier label. Tray icon layout is
/// computed solely from <see cref="Windows"/> plus user settings.
/// </summary>
/// <param name="ProviderName">Display name of the provider that produced this result.</param>
/// <param name="Plan">Plan/tier label (e.g. "Pro", "Max", "Free"), or <see langword="null"/>.</param>
/// <param name="Windows">Usage windows for the tooltip and threshold checks.</param>
/// <param name="Notice">Optional card-level notice shown once above the usage windows.</param>
public sealed record MetricResult(
    string ProviderName,
    string? Plan,
    IReadOnlyList<UsageWindow> Windows,
    string? Notice = null) : ProviderResult(ProviderName);

/// <summary>A balance provider's result: a pre-formatted balance string (e.g. "$12.34").</summary>
/// <param name="ProviderName">Display name of the provider that produced this result.</param>
/// <param name="BalanceText">Display-ready balance, e.g. "$12.34" or "$1.00 / ¥7.00".</param>
/// <param name="UsdAmount">Raw USD balance for threshold hiding, or <see langword="null"/>.</param>
/// <param name="CnyAmount">Raw CNY balance for threshold hiding (DeepSeek only), or <see langword="null"/>.</param>
public sealed record BalanceResult(
    string ProviderName,
    string BalanceText,
    decimal? UsdAmount = null,
    decimal? CnyAmount = null) : ProviderResult(ProviderName);
