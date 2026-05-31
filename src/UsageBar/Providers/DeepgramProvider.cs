using System.Globalization;
using System.Text.Json;
using UsageBar.Domain;

namespace UsageBar.Providers;

internal sealed class DeepgramProvider(HttpClient httpClient) : IUsageProvider
{
    public string Name => "Deepgram";

    public async Task<ProviderResult?> GetUsageAsync(ProviderCredentials credentials, CancellationToken cancellationToken)
    {
        var apiKey = credentials.DeepgramApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var projectId = await GetFirstProjectIdAsync(apiKey, cancellationToken).ConfigureAwait(false);
        var balance = await GetBalanceAsync(apiKey, projectId, cancellationToken).ConfigureAwait(false);

        return new ProviderResult([new UsageBlock("Deepgram:", FormatCurrency(balance), Inline: true)]);
    }

    private async Task<string> GetFirstProjectIdAsync(string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.deepgram.com/v1/projects");
        AddHeaders(request, apiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var project in EnumerateProjects(document.RootElement))
        {
            var projectId = ProviderJson.GetString(project, "project_id", "projectId", "id");
            if (!string.IsNullOrWhiteSpace(projectId))
            {
                return projectId;
            }
        }

        throw new InvalidOperationException("Deepgram response did not contain a project_id.");
    }

    private async Task<decimal> GetBalanceAsync(string apiKey, string projectId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.deepgram.com/v1/projects/{Uri.EscapeDataString(projectId)}/balances");
        AddHeaders(request, apiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var total = 0m;
        var found = false;

        foreach (var balance in EnumerateBalances(document.RootElement))
        {
            var amount = ProviderJson.GetDecimal(balance, "amount") ??
                ProviderJson.GetDecimal(balance, "balance") ??
                ProviderJson.GetDecimal(balance, "total_balance");

            if (amount is null)
            {
                continue;
            }

            var units = ProviderJson.GetString(balance, "units", "currency");
            if (!string.IsNullOrWhiteSpace(units) &&
                !string.Equals(units, "usd", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(units, "USD", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            total += amount.Value;
            found = true;
        }

        if (!found)
        {
            throw new InvalidOperationException("Deepgram response did not contain a balance amount.");
        }

        return total;
    }

    private static IEnumerable<JsonElement> EnumerateProjects(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        if (ProviderJson.TryGetProperty(root, "projects", out var projects) && projects.ValueKind == JsonValueKind.Array)
        {
            foreach (var project in projects.EnumerateArray())
            {
                yield return project;
            }
        }
    }

    private static IEnumerable<JsonElement> EnumerateBalances(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        if (ProviderJson.TryGetProperty(root, "balances", out var balances) && balances.ValueKind == JsonValueKind.Array)
        {
            foreach (var balance in balances.EnumerateArray())
            {
                yield return balance;
            }

            yield break;
        }

        yield return root;
    }

    private static void AddHeaders(HttpRequestMessage request, string apiKey)
    {
        request.Headers.TryAddWithoutValidation("Authorization", $"Token {apiKey}");
        request.Headers.Accept.ParseAdd("application/json");
    }

    private static string FormatCurrency(decimal value)
    {
        return string.Create(CultureInfo.InvariantCulture, $"${value:0.00}");
    }
}
