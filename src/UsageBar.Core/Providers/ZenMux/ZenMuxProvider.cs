using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;
/// <summary>Reports the ZenMux PAYG USD balance.</summary>
public sealed class ZenMuxProvider(HttpClient httpClient) : BalanceUsageProvider(httpClient)
{
    public override ProviderDescriptor Descriptor { get; } = new("ZenMux", 111, ProviderAuthenticationKind.ApiKey, CredentialNames.ZenMux, SettingsOrder: 5);
    protected override string CredentialName => CredentialNames.ZenMux;

    protected override async Task<BalanceFetchResult> FetchBalanceAsync(HttpClient httpClient, string apiKey, CancellationToken cancellationToken)
    {
        using (var document = await ProviderHttp.GetJsonWithBearerAsync(httpClient, "https://zenmux.ai/api/v1/management/payg/balance", apiKey, cancellationToken).ConfigureAwait(false))
        {
            if (!ProviderJson.TryGetProperty(document.RootElement, "data", out var data))
            {
                throw new ProviderException("ZenMux response did not contain data.");
            }

            var totalCredits = ProviderJson.GetDecimal(data, "total_credits");
            if (totalCredits is null)
            {
                throw new ProviderException("ZenMux response did not contain data.total_credits.");
            }

            return new BalanceFetchResult(UsageFormatting.Currency(totalCredits.Value), totalCredits.Value);
        }
    }
}