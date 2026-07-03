using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>Reports the OpenAI account credit grant balance (total_available USD).</summary>
public sealed class OpenAIProvider(HttpClient httpClient) : BalanceUsageProvider(httpClient)
{
    public override ProviderDescriptor Descriptor { get; } = new("OpenAI", DisplayOrder: 105);

    protected override string CredentialName => CredentialNames.OpenAI;

    protected override async Task<BalanceFetchResult> FetchBalanceAsync(HttpClient httpClient, string apiKey, CancellationToken cancellationToken)
    {
        using var document = await ProviderHttp.GetJsonWithBearerAsync(httpClient, "https://api.openai.com/v1/dashboard/billing/credit_grants", apiKey, cancellationToken).ConfigureAwait(false);

        var totalAvailable = ProviderJson.GetDecimal(document.RootElement, "total_available");
        if (totalAvailable is null)
        {
            throw new ProviderException("OpenAI response did not contain total_available.");
        }

        return new BalanceFetchResult(UsageFormatting.Currency(totalAvailable.Value), totalAvailable.Value);
    }
}
