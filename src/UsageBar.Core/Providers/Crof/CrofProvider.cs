using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;
/// <summary>Reports the Crof credit balance (USD).</summary>
public sealed class CrofProvider(HttpClient httpClient) : BalanceUsageProvider(httpClient)
{
    public override ProviderDescriptor Descriptor { get; } = new("Crof", 112, ProviderAuthenticationKind.ApiKey, CredentialNames.Crof, SettingsOrder: 13);
    protected override string CredentialName => CredentialNames.Crof;

    protected override async Task<BalanceFetchResult> FetchBalanceAsync(HttpClient httpClient, string apiKey, CancellationToken cancellationToken)
    {
        using (var document = await ProviderHttp.GetJsonWithBearerAsync(httpClient, "https://crof.ai/usage_api/", apiKey, cancellationToken).ConfigureAwait(false))
        {
            var credits = ProviderJson.GetDecimal(document.RootElement, "credits");
            if (credits is null)
            {
                throw new ProviderException("Crof response did not contain credits.");
            }

            return new BalanceFetchResult(UsageFormatting.Currency(credits.Value), credits.Value);
        }
    }
}