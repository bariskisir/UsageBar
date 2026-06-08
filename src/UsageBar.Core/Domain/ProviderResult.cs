namespace UsageBar.Domain;

/// <summary>
/// The outcome of a single provider refresh. Construct via <see cref="Metric"/> for
/// window/percent providers or <see cref="Balance"/> for balance providers.
/// </summary>
public sealed record ProviderResult
{
    private ProviderResult(
        string providerName,
        ProviderCategory category,
        string? plan,
        IReadOnlyList<UsageWindow> windows,
        string? balanceText)
    {
        ProviderName = providerName;
        Category = category;
        Plan = plan;
        Windows = windows;
        BalanceText = balanceText;
    }

    /// <summary>Display name of the provider that produced this result.</summary>
    public string ProviderName { get; }

    /// <summary>Whether this result carries usage windows or a balance.</summary>
    public ProviderCategory Category { get; }

    /// <summary>Plan/tier label for metric providers; <see langword="null"/> otherwise.</summary>
    public string? Plan { get; }

    /// <summary>Usage windows for metric providers; empty for balance providers.</summary>
    public IReadOnlyList<UsageWindow> Windows { get; }

    /// <summary>Formatted balance (e.g. "$12.34") for balance providers; <see langword="null"/> otherwise.</summary>
    public string? BalanceText { get; }

    /// <summary>Creates a metric result from usage windows and an optional plan label.</summary>
    public static ProviderResult Metric(string providerName, string? plan, IReadOnlyList<UsageWindow> windows) =>
        new(providerName, ProviderCategory.Metric, plan, windows, balanceText: null);

    /// <summary>Creates a balance result from a pre-formatted balance string.</summary>
    public static ProviderResult Balance(string providerName, string balanceText) =>
        new(providerName, ProviderCategory.Balance, plan: null, windows: [], balanceText);
}
