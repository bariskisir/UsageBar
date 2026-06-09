using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>
/// Base class for providers that report a currency balance. Subclasses implement
/// <see cref="FetchBalanceAsync"/> and declare <see cref="Name"/> and
/// <see cref="CredentialName"/>; this base handles the credential check and result assembly.
/// </summary>
/// <remarks>
/// Adding a new balance provider is intentionally small: create a folder under
/// <c>Providers/</c>, derive from this class, return a display-ready balance string from
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

    public async Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var apiKey = context.GetApiKey(CredentialName);
        if (apiKey is null)
        {
            return null;
        }

        var balanceText = await FetchBalanceAsync(_httpClient, apiKey, cancellationToken).ConfigureAwait(false);
        return new BalanceResult(Descriptor.Name, balanceText);
    }

    /// <summary>
    /// Fetches the remaining balance as a display-ready string (e.g. <c>$12.34</c>).
    /// Throws on API/parse failures.
    /// </summary>
    protected abstract Task<string> FetchBalanceAsync(HttpClient httpClient, string apiKey, CancellationToken cancellationToken);
}
