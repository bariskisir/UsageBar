using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>Reports the OpenRouter remaining credit balance (credits minus usage).</summary>
public sealed class OpenRouterProvider(HttpClient httpClient) : BalanceUsageProvider(httpClient)
{
    public override ProviderDescriptor Descriptor { get; } = new("OpenRouter", DisplayOrder: 110);

    protected override string CredentialName => CredentialNames.OpenRouter;

    protected override async Task<BalanceFetchResult> FetchBalanceAsync(HttpClient httpClient, string apiKey, CancellationToken cancellationToken)
    {
        using var document = await ProviderHttp.GetJsonWithBearerAsync(httpClient, "https://openrouter.ai/api/v1/credits", apiKey, cancellationToken).ConfigureAwait(false);

        if (!ProviderJson.TryGetProperty(document.RootElement, "data", out var data))
        {
            throw new ProviderException("OpenRouter response did not contain data.");
        }

        var totalCredits = ProviderJson.GetDecimal(data, "total_credits");
        var totalUsage = ProviderJson.GetDecimal(data, "total_usage");

        if (totalCredits is null || totalUsage is null)
        {
            throw new ProviderException("OpenRouter response did not contain total_credits and total_usage.");
        }

        var remaining = totalCredits.Value - totalUsage.Value;
        return new BalanceFetchResult(UsageFormatting.Currency(remaining), remaining);
    }
}
