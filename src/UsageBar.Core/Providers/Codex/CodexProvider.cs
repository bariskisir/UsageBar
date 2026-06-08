using System.Net.Http.Headers;
using System.Text.Json;
using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>
/// Reports Codex (ChatGPT) rate-limit usage as Session (5h) and Weekly (7d) windows,
/// plus the account plan/tier. Credentials come from the local Codex CLI auth file.
/// </summary>
public sealed class CodexProvider(HttpClient httpClient, ICodexAuthReader authReader) : IUsageProvider
{
    private const string UsageEndpoint = "https://chatgpt.com/backend-api/wham/usage";

    public string Name => "Codex";

    public ProviderCategory Category => ProviderCategory.Metric;

    public async Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var auth = authReader.Read();
        if (auth is null)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        request.Headers.TryAddWithoutValidation("originator", "codex_cli_rs");
        request.Headers.TryAddWithoutValidation("chatgpt-account-id", auth.AccountId);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var plan = PlanLabel(ProviderJson.GetString(document.RootElement, "plan_type"));
        var rateLimit = GetRateLimit(document.RootElement);

        var windows = new List<UsageWindow>(2);
        if (ReadWindow(rateLimit, "primary_window", "Session", context.Now) is { } primary)
        {
            windows.Add(primary);
        }

        if (ReadWindow(rateLimit, "secondary_window", "Weekly", context.Now) is { } secondary)
        {
            windows.Add(secondary);
        }

        if (windows.Count == 0)
        {
            throw new ProviderException("Codex response did not contain usable rate limit windows.");
        }

        return ProviderResult.Metric(Name, plan, windows);
    }

    /// <summary>Maps a Codex <c>plan_type</c> to a short plan/tier label.</summary>
    private static string? PlanLabel(string? planType)
    {
        if (string.IsNullOrWhiteSpace(planType))
        {
            return null;
        }

        return planType.ToLowerInvariant() switch
        {
            "free" => "Free",
            "plus" => "Plus",
            "pro" => "Pro",
            "pro_lite" or "prolite" or "pro-lite" => "Pro Lite",
            "go" => "Go",
            "team" => "Team",
            "business" => "Business",
            "enterprise" => "Enterprise",
            "education" or "edu" => "Education",
            "guest" => "Guest",
            _ => Capitalize(planType),
        };
    }

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static JsonElement GetRateLimit(JsonElement root)
    {
        if (ProviderJson.TryGetProperty(root, "rate_limit", out var rateLimit) && rateLimit.ValueKind == JsonValueKind.Object)
        {
            return rateLimit;
        }

        if (ProviderJson.TryGetProperty(root, "additional_rate_limits", out var additional) &&
            additional.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in additional.EnumerateArray())
            {
                if (ProviderJson.TryGetProperty(item, "rate_limit", out var nested) && nested.ValueKind == JsonValueKind.Object)
                {
                    return nested;
                }
            }
        }

        throw new ProviderException("Codex response did not contain rate_limit.");
    }

    private static UsageWindow? ReadWindow(JsonElement rateLimit, string propertyName, string label, DateTimeOffset now)
    {
        if (!ProviderJson.TryGetProperty(rateLimit, propertyName, out var window) || window.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var usedPercent = ProviderJson.GetDouble(window, "used_percent");
        var resetAt = ProviderJson.GetDouble(window, "reset_at");

        if (usedPercent is null || resetAt is null)
        {
            return null;
        }

        var resetText = UsageFormatting.ResetDuration(FromEpoch(resetAt.Value) - now);
        return new UsageWindow("Codex", label, Math.Clamp(usedPercent.Value, 0, 100), resetText);
    }

    private static DateTimeOffset FromEpoch(double epoch)
    {
        var seconds = epoch > 10_000_000_000 ? epoch / 1000 : epoch;
        return DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(seconds));
    }
}
