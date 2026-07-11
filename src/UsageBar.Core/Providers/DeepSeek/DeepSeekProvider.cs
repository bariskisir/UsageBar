using System.Text.Json;
using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;
/// <summary>Reports the DeepSeek account USD balance.</summary>
public sealed class DeepSeekProvider(HttpClient httpClient) : BalanceUsageProvider(httpClient)
{
    public override ProviderDescriptor Descriptor { get; } = new("DeepSeek", 100, ProviderAuthenticationKind.ApiKey, CredentialNames.DeepSeek, SettingsOrder: 3);
    protected override string CredentialName => CredentialNames.DeepSeek;

    protected override async Task<BalanceFetchResult> FetchBalanceAsync(HttpClient httpClient, string apiKey, CancellationToken cancellationToken)
    {
        using (var document = await ProviderHttp.GetJsonWithBearerAsync(httpClient, "https://api.deepseek.com/user/balance", apiKey, cancellationToken).ConfigureAwait(false))
        {
            if (!ProviderJson.TryGetProperty(document.RootElement, "balance_infos", out var balanceInfos) || balanceInfos.ValueKind != JsonValueKind.Array)
            {
                throw new ProviderException("DeepSeek response did not contain balance_infos.");
            }

            decimal? usd = null;
            decimal? cny = null;
            foreach (var balanceInfo in balanceInfos.EnumerateArray())
            {
                var amount = ProviderJson.GetDecimal(balanceInfo, "total_balance");
                if (amount is null)
                {
                    continue;
                }

                var currency = ProviderJson.GetString(balanceInfo, "currency");
                if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase))
                {
                    usd = amount;
                }
                else if (string.Equals(currency, "CNY", StringComparison.OrdinalIgnoreCase))
                {
                    cny = amount;
                }
            }

            // Show USD always (the primary balance); add CNY only when it is non-zero.
            var parts = new List<string>(2);
            if (usd is not null)
            {
                parts.Add(UsageFormatting.Currency(usd.Value));
            }

            if (cny is { } cnyValue && cnyValue != 0)
            {
                parts.Add(UsageFormatting.Currency(cnyValue, "¥"));
            }

            if (parts.Count == 0)
            {
                throw new ProviderException("DeepSeek response did not contain a USD or CNY balance.");
            }

            return new BalanceFetchResult(string.Join(" / ", parts), usd, cny);
        }
    }
}