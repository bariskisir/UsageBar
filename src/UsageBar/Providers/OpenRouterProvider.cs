using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using UsageBar.Domain;

namespace UsageBar.Providers;

internal sealed class OpenRouterProvider(HttpClient httpClient) : IUsageProvider
{
    public string Name => "OpenRouter";

    public async Task<ProviderResult?> GetUsageAsync(ProviderCredentials credentials, CancellationToken cancellationToken)
    {
        var apiKey = credentials.OpenRouterApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/credits");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!ProviderJson.TryGetProperty(document.RootElement, "data", out var data))
        {
            throw new InvalidOperationException("OpenRouter response did not contain data.");
        }

        var totalCredits = ProviderJson.GetDecimal(data, "total_credits");
        var totalUsage = ProviderJson.GetDecimal(data, "total_usage");

        if (totalCredits is null || totalUsage is null)
        {
            throw new InvalidOperationException("OpenRouter response did not contain total_credits and total_usage.");
        }

        var remaining = totalCredits.Value - totalUsage.Value;
        return new ProviderResult([new UsageBlock("OpenRouter:", FormatCurrency(remaining), Inline: true)]);
    }

    private static string FormatCurrency(decimal value)
    {
        return string.Create(CultureInfo.InvariantCulture, $"${value:0.00}");
    }
}
