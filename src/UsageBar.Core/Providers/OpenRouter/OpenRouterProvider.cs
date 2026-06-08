using System.Net.Http.Headers;
using System.Text.Json;
using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>Reports the OpenRouter remaining credit balance (credits minus usage).</summary>
public sealed class OpenRouterProvider(HttpClient httpClient) : BalanceUsageProvider(httpClient)
{
    public override string Name => "OpenRouter";

    protected override string CredentialName => CredentialNames.OpenRouter;

    protected override async Task<string> FetchBalanceAsync(HttpClient httpClient, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/credits");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

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

        return UsageFormatting.Currency(totalCredits.Value - totalUsage.Value);
    }
}
