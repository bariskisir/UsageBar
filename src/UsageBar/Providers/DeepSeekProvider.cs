using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using UsageBar.Domain;

namespace UsageBar.Providers;

internal sealed class DeepSeekProvider(HttpClient httpClient) : IUsageProvider
{
    public string Name => "DeepSeek";

    public async Task<ProviderResult?> GetUsageAsync(ProviderCredentials credentials, CancellationToken cancellationToken)
    {
        var apiKey = credentials.DeepSeekApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!ProviderJson.TryGetProperty(document.RootElement, "balance_infos", out var balanceInfos) ||
            balanceInfos.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("DeepSeek response did not contain balance_infos.");
        }

        foreach (var balanceInfo in balanceInfos.EnumerateArray())
        {
            var currency = ProviderJson.GetString(balanceInfo, "currency");
            if (!string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var totalBalance = ProviderJson.GetDecimal(balanceInfo, "total_balance");
            if (totalBalance is null)
            {
                throw new InvalidOperationException("DeepSeek USD balance did not contain total_balance.");
            }

            return new ProviderResult([new UsageBlock("DeepSeek:", FormatCurrency(totalBalance.Value), Inline: true)]);
        }

        throw new InvalidOperationException("DeepSeek response did not contain a USD balance.");
    }

    private static string FormatCurrency(decimal value)
    {
        return string.Create(CultureInfo.InvariantCulture, $"${value:0.00}");
    }
}
