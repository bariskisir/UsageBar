using System.Net.Http.Headers;
using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>Reports the Moonshot account available USD balance.</summary>
public sealed class MoonshotProvider(HttpClient httpClient) : BalanceUsageProvider(httpClient)
{
    public override ProviderDescriptor Descriptor { get; } = new(@"Moonshot (Kimi)", DisplayOrder: 115);

    protected override string CredentialName => CredentialNames.Moonshot;

    protected override async Task<string> FetchBalanceAsync(HttpClient httpClient, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.moonshot.ai/v1/users/me/balance");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var document = await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false);

        if (!ProviderJson.TryGetProperty(document.RootElement, "data", out var data))
        {
            throw new ProviderException("Moonshot response did not contain data.");
        }

        var availableBalance = ProviderJson.GetDecimal(data, "available_balance");
        if (availableBalance is null)
        {
            throw new ProviderException("Moonshot response did not contain available_balance.");
        }

        return UsageFormatting.Currency(availableBalance.Value);
    }
}
