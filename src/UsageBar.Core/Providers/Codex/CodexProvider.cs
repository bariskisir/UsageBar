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

    public ProviderDescriptor Descriptor { get; } = new("Codex", DisplayOrder: 0);

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

        using var document = await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false);

        var plan = PlanLabel(ProviderJson.GetString(document.RootElement, "plan_type"));
        var rateLimit = GetRateLimit(document.RootElement);

        var session = ReadWindow(rateLimit, "primary_window", "Session", Descriptor.Name, context.Now);
        var weekly = ReadWindow(rateLimit, "secondary_window", "Weekly", Descriptor.Name, context.Now);

        var windows = MetricWindows.Require(Descriptor.Name, session, weekly);
        return new MetricResult(Descriptor.Name, plan, windows, BuildIconBars(plan, session, weekly));
    }

    /// <summary>
    /// Maps Codex windows to tray bars. Free (or session-less) accounts show a single bar from the
    /// Weekly window at double weight, so it reads as one full band beside other providers' weight-1
    /// bars; paid accounts show Session + Weekly at equal weight.
    /// </summary>
    private static IReadOnlyList<IconBar> BuildIconBars(string? plan, UsageWindow? session, UsageWindow? weekly)
    {
        var freeLike = string.Equals(plan, "Free", StringComparison.OrdinalIgnoreCase) || session is null;
        if (!freeLike)
        {
            return MetricWindows.EqualWeightBars(session, weekly);
        }

        var single = weekly ?? session;
        return single is null ? [] : [new IconBar(single.UsedPercent, 2.0)];
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
            _ => UsageFormatting.Capitalize(planType),
        };
    }

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

    private static UsageWindow? ReadWindow(JsonElement rateLimit, string propertyName, string label, string providerName, DateTimeOffset now)
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
        return new UsageWindow(providerName, label, Math.Clamp(usedPercent.Value, 0, 100), resetText);
    }

    private static DateTimeOffset FromEpoch(double epoch)
    {
        var seconds = epoch > 10_000_000_000 ? epoch / 1000 : epoch;
        return DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(seconds));
    }
}
