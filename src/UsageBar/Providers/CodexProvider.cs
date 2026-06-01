using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using UsageBar.Domain;

namespace UsageBar.Providers;

internal sealed class CodexProvider(HttpClient httpClient) : IUsageProvider
{
    private const string UsageEndpoint = "https://chatgpt.com/backend-api/wham/usage";

    public string Name => "Codex";

    public async Task<ProviderResult?> GetUsageAsync(ProviderCredentials credentials, CancellationToken cancellationToken)
    {
        var auth = ReadAuth();
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

        var plan = CodexPlanLabel(ProviderJson.GetString(document.RootElement, "plan_type"));

        var rateLimit = GetRateLimit(document.RootElement);
        var primary = GetWindow(rateLimit, "primary_window");
        var secondary = GetWindow(rateLimit, "secondary_window");

        var blocks = new List<UsageBlock>();
        if (primary is not null)
        {
            blocks.Add(new UsageBlock("Codex Session:", FormatUsageLine(primary.Value), Inline: true));
        }

        if (secondary is not null)
        {
            blocks.Add(new UsageBlock("Codex Weekly:", FormatUsageLine(secondary.Value), Inline: true));
        }

        if (blocks.Count == 0)
        {
            throw new InvalidOperationException("Codex response did not contain usable rate limit windows.");
        }

        var windows = new List<UsageBarWindow>();
        if (primary is not null)
        {
            windows.Add(new UsageBarWindow("Codex", "Session", Math.Clamp(primary.Value.UsedPercent, 0, 100)));
        }

        if (secondary is not null)
        {
            windows.Add(new UsageBarWindow("Codex", "Weekly", Math.Clamp(secondary.Value.UsedPercent, 0, 100)));
        }

        return new ProviderResult(blocks, windows, plan);
    }

    /// <summary>
    /// Maps a Codex <c>plan_type</c> to a short plan/tier label shown in the tooltip.
    /// </summary>
    private static string? CodexPlanLabel(string? planType)
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
            _ => Capitalize(planType)
        };
    }

    /// <summary>Capitalises the first character of <paramref name="s"/>.</summary>
    private static string Capitalize(string s)
    {
        if (s.Length == 0)
        {
            return s;
        }

        return char.ToUpperInvariant(s[0]) + s[1..];
    }

    private static CodexAuth? ReadAuth()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            return null;
        }

        var authPath = Path.Combine(userProfile, ".codex", "auth.json");
        if (!File.Exists(authPath))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(authPath));
        var root = document.RootElement;

        var tokenSource = ProviderJson.TryGetProperty(root, "tokens", out var tokens) ? tokens : root;
        var accessToken = ProviderJson.GetString(tokenSource, "access_token");
        var accountId = ProviderJson.GetString(tokenSource, "account_id");

        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(accountId))
        {
            return null;
        }

        return new CodexAuth(accessToken, accountId);
    }

    private static JsonElement GetRateLimit(JsonElement root)
    {
        if (ProviderJson.TryGetProperty(root, "rate_limit", out var rateLimit) && rateLimit.ValueKind == JsonValueKind.Object)
        {
            return rateLimit;
        }

        if (ProviderJson.TryGetProperty(root, "additional_rate_limits", out var additionalRateLimits) &&
            additionalRateLimits.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in additionalRateLimits.EnumerateArray())
            {
                if (ProviderJson.TryGetProperty(item, "rate_limit", out var nestedRateLimit) &&
                    nestedRateLimit.ValueKind == JsonValueKind.Object)
                {
                    return nestedRateLimit;
                }
            }
        }

        throw new InvalidOperationException("Codex response did not contain rate_limit.");
    }

    private static CodexWindow? GetWindow(JsonElement rateLimit, string propertyName)
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

        return new CodexWindow(Math.Clamp(usedPercent.Value, 0, 100), FromEpoch(resetAt.Value));
    }

    private static DateTimeOffset FromEpoch(double epoch)
    {
        var seconds = epoch > 10_000_000_000 ? epoch / 1000 : epoch;
        return DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(seconds));
    }

    private static string FormatUsageLine(CodexWindow window)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{window.UsedPercent:0.#}%, {FormatResetDuration(window.ResetAt - DateTimeOffset.Now)}");
    }

    private static string FormatResetDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return "now";
        }

        if (duration >= TimeSpan.FromDays(1))
        {
            duration = TimeSpan.FromMinutes(Math.Ceiling(duration.TotalMinutes));
            return $"{(int)duration.TotalDays}d {duration.Hours}h";
        }

        var totalMinutes = Math.Max(1, (int)Math.Ceiling(duration.TotalMinutes));
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;

        if (hours >= 1)
        {
            return $"{hours}h {minutes}m";
        }

        return $"{minutes}m";
    }

    private sealed record CodexAuth(string AccessToken, string AccountId);

    private readonly record struct CodexWindow(double UsedPercent, DateTimeOffset ResetAt);
}
