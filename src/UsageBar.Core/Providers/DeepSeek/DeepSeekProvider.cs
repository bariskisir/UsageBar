using System.Net.Http.Headers;
using System.Text.Json;
using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>Reports the DeepSeek account USD balance.</summary>
public sealed class DeepSeekProvider(HttpClient httpClient) : BalanceUsageProvider(httpClient)
{
    public override ProviderDescriptor Descriptor { get; } = new("DeepSeek", DisplayOrder: 100);

    protected override string CredentialName => CredentialNames.DeepSeek;

    protected override async Task<string> FetchBalanceAsync(HttpClient httpClient, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var document = await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false);

        if (!ProviderJson.TryGetProperty(document.RootElement, "balance_infos", out var balanceInfos) ||
            balanceInfos.ValueKind != JsonValueKind.Array)
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

        return string.Join(" / ", parts);
    }
}
