using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>Reports the Moonshot account available USD balance.</summary>
public sealed class MoonshotProvider(HttpClient httpClient) : BalanceUsageProvider(httpClient)
{
    public override ProviderDescriptor Descriptor { get; } = new(
        "Moonshot (Kimi)", 115, ProviderAuthenticationKind.ApiKey, CredentialNames.Moonshot, SettingsOrder: 6);

    protected override string CredentialName => CredentialNames.Moonshot;

    protected override async Task<BalanceFetchResult> FetchBalanceAsync(HttpClient httpClient, string apiKey, CancellationToken cancellationToken)
    {
        using var document = await ProviderHttp.GetJsonWithBearerAsync(httpClient, "https://api.moonshot.ai/v1/users/me/balance", apiKey, cancellationToken).ConfigureAwait(false);

        if (!ProviderJson.TryGetProperty(document.RootElement, "data", out var data))
        {
            throw new ProviderException("Moonshot response did not contain data.");
        }

        var availableBalance = ProviderJson.GetDecimal(data, "available_balance");
        if (availableBalance is null)
        {
            throw new ProviderException("Moonshot response did not contain available_balance.");
        }

        return new BalanceFetchResult(UsageFormatting.Currency(availableBalance.Value), availableBalance.Value);
    }
}
