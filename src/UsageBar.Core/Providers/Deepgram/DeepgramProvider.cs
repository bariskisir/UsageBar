using System.Text.Json;
using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>
/// Reports the Deepgram USD balance. Deepgram exposes balances per project, so this
/// resolves the first project and sums its USD balances.
/// </summary>
public sealed class DeepgramProvider(HttpClient httpClient) : BalanceUsageProvider(httpClient)
{
    public override ProviderDescriptor Descriptor { get; } = new("Deepgram", DisplayOrder: 120);

    protected override string CredentialName => CredentialNames.Deepgram;

    protected override async Task<string> FetchBalanceAsync(HttpClient httpClient, string apiKey, CancellationToken cancellationToken)
    {
        var projectId = await GetFirstProjectIdAsync(httpClient, apiKey, cancellationToken).ConfigureAwait(false);
        var balance = await GetBalanceAsync(httpClient, apiKey, projectId, cancellationToken).ConfigureAwait(false);
        return UsageFormatting.Currency(balance);
    }

    private static async Task<string> GetFirstProjectIdAsync(HttpClient httpClient, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.deepgram.com/v1/projects");
        AddHeaders(request, apiKey);

        using var document = await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false);

        foreach (var project in EnumerateProjects(document.RootElement))
        {
            var projectId = ProviderJson.GetString(project, "project_id", "projectId", "id");
            if (!string.IsNullOrWhiteSpace(projectId))
            {
                return projectId;
            }
        }

        throw new ProviderException("Deepgram response did not contain a project_id.");
    }

    private static async Task<decimal> GetBalanceAsync(HttpClient httpClient, string apiKey, string projectId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.deepgram.com/v1/projects/{Uri.EscapeDataString(projectId)}/balances");
        AddHeaders(request, apiKey);

        using var document = await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false);

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
            if (!string.IsNullOrWhiteSpace(units) && !string.Equals(units, "usd", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            total += amount.Value;
            found = true;
        }

        if (!found)
        {
            throw new ProviderException("Deepgram response did not contain a balance amount.");
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
}
