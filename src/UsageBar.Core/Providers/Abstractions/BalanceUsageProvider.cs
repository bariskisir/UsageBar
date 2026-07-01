using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>
/// Returned by <see cref="BalanceUsageProvider.FetchBalanceAsync"/> so the base class can
/// assemble a <see cref="BalanceResult"/> with both the display text and the raw amounts
/// needed for threshold-based hiding.
/// </summary>
/// <param name="DisplayText">Display-ready balance, e.g. <c>"$12.34"</c> or <c>"$1.00 / ¥7.00"</c>.</param>
/// <param name="UsdAmount">Raw USD balance for threshold hiding, or <see langword="null"/>.</param>
/// <param name="CnyAmount">Raw CNY balance (DeepSeek), or <see langword="null"/>.</param>
public sealed record BalanceFetchResult(string DisplayText, decimal? UsdAmount = null, decimal? CnyAmount = null);

/// <summary>
/// Base class for providers that report a currency balance. Subclasses implement
/// <see cref="FetchBalanceAsync"/> and declare <see cref="Name"/> and
/// <see cref="CredentialName"/>; this base handles the credential check and result assembly.
/// </summary>
/// <remarks>
/// Adding a new balance provider is intentionally small: create a folder under
/// <c>Providers/</c>, derive from this class, return a <see cref="BalanceFetchResult"/> from
/// <see cref="FetchBalanceAsync"/> (use <see cref="UsageFormatting"/> for currency formatting),
/// and register it as <see cref="IUsageProvider"/>.
/// </remarks>
public abstract class BalanceUsageProvider : IUsageProvider
{
    private readonly HttpClient _httpClient;

    protected BalanceUsageProvider(HttpClient httpClient) => _httpClient = httpClient;

    /// <summary>Identity and presentation metadata; subclasses set the display order.</summary>
    public abstract ProviderDescriptor Descriptor { get; }

    /// <summary>Credential/environment-variable name that enables this provider.</summary>
    protected abstract string CredentialName { get; }

    public void RefreshEnabled(ProviderQueryContext context) =>
        Descriptor.IsEnabled = !string.IsNullOrEmpty(context.GetApiKey(CredentialName));

    public async Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var apiKey = context.GetApiKey(CredentialName);
        if (apiKey is null)
        {
            return null;
        }

        var fetchResult = await FetchBalanceAsync(_httpClient, apiKey, cancellationToken).ConfigureAwait(false);
        return new BalanceResult(Descriptor.Name, fetchResult.DisplayText, fetchResult.UsdAmount, fetchResult.CnyAmount);
    }

    /// <summary>
    /// Fetches the remaining balance as a <see cref="BalanceFetchResult"/> with both the
    /// display-ready text and the raw amounts needed for threshold-based hiding.
    /// Throws on API/parse failures.
    /// </summary>
    protected abstract Task<BalanceFetchResult> FetchBalanceAsync(HttpClient httpClient, string apiKey, CancellationToken cancellationToken);
}
