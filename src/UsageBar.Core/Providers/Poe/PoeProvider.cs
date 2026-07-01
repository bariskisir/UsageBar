using System.Net.Http.Headers;
using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>Reports the Poe point balance.</summary>
public sealed class PoeProvider(HttpClient httpClient) : BalanceUsageProvider(httpClient)
{
    public override ProviderDescriptor Descriptor { get; } = new("Poe", DisplayOrder: 118);

    protected override string CredentialName => CredentialNames.Poe;

    protected override async Task<BalanceFetchResult> FetchBalanceAsync(HttpClient httpClient, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.poe.com/usage/current_balance");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var document = await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false);

        var balance = ProviderJson.GetDecimal(document.RootElement, "current_point_balance");
        if (balance is null)
        {
            throw new ProviderException("Poe response did not contain current_point_balance.");
        }

        var text = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{balance.Value:0.00} pts");
        return new BalanceFetchResult(text);
    }
}
