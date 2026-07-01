using System.Net.Http.Headers;
using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>Reports the Crof credit balance (USD).</summary>
public sealed class CrofProvider(HttpClient httpClient) : BalanceUsageProvider(httpClient)
{
    public override ProviderDescriptor Descriptor { get; } = new("Crof", DisplayOrder: 112);

    protected override string CredentialName => CredentialNames.Crof;

    protected override async Task<BalanceFetchResult> FetchBalanceAsync(HttpClient httpClient, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://crof.ai/usage_api/");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var document = await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false);

        var credits = ProviderJson.GetDecimal(document.RootElement, "credits");
        if (credits is null)
        {
            throw new ProviderException("Crof response did not contain credits.");
        }

        return new BalanceFetchResult(UsageFormatting.Currency(credits.Value), credits.Value);
    }
}
