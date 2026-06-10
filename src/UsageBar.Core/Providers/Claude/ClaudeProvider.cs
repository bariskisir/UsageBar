using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>
/// Reports Claude usage as Session (five_hour) and Weekly (seven_day) windows, plus the
/// subscription tier. Credentials come from the local Claude credentials file.
/// </summary>
public sealed class ClaudeProvider(HttpClient httpClient, IClaudeAuthReader authReader) : IUsageProvider
{
    private const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";
    private const string BetaHeader = "oauth-2025-04-20";
    private const string ClaudeCodeUserAgent = "claude-code/2.1.0";

    public ProviderDescriptor Descriptor { get; } = new("Claude", DisplayOrder: 10);

    public async Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var auth = authReader.Read();
        if (auth is null)
        {
            return null;
        }

        var plan = PlanLabel(auth.SubscriptionType ?? auth.RateLimitTier);

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("anthropic-beta", BetaHeader);
        request.Headers.TryAddWithoutValidation("User-Agent", ClaudeCodeUserAgent);

        using var document = await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false);

        var session = ReadWindow(document.RootElement, "five_hour", "Session", Descriptor.Name, context.Now);
        var weekly = ReadWindow(document.RootElement, "seven_day", "Weekly", Descriptor.Name, context.Now);

        var windows = MetricWindows.Require(Descriptor.Name, session, weekly);
        return new MetricResult(Descriptor.Name, plan, windows, MetricWindows.EqualWeightBars(session, weekly));
    }

    /// <summary>Maps a Claude subscription tier to a short plan label.</summary>
    private static string? PlanLabel(string? tier)
    {
        if (string.IsNullOrWhiteSpace(tier))
        {
            return null;
        }

        var normalized = tier.Trim().ToLowerInvariant();

        return normalized switch
        {
            _ when normalized.Contains("max") => "Max",
            _ when normalized.Contains("pro") => "Pro",
            _ when normalized.Contains("team") => "Team",
            _ when normalized.Contains("enterprise") => "Enterprise",
            _ when normalized.Contains("free") => "Free",
            "default_claude_ai" => "Claude AI",
            _ => UsageFormatting.Capitalize(normalized),
        };
    }

    private static UsageWindow? ReadWindow(JsonElement root, string propertyName, string label, string providerName, DateTimeOffset now)
    {
        if (!ProviderJson.TryGetProperty(root, propertyName, out var window) || window.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var utilization = ProviderJson.GetDouble(window, "utilization");
        if (utilization is null)
        {
            return null;
        }

        var resetTime = ParseResetTime(ProviderJson.GetString(window, "resets_at"));
        var resetText = resetTime is { } reset ? UsageFormatting.ResetDuration(reset - now) : null;

        return new UsageWindow(providerName, label, Math.Clamp(utilization.Value, 0, 100), resetText);
    }

    private static DateTimeOffset? ParseResetTime(string? iso8601)
    {
        if (string.IsNullOrWhiteSpace(iso8601))
        {
            return null;
        }

        return DateTimeOffset.TryParse(iso8601, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}
