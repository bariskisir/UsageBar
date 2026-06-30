using System.Globalization;
using System.Net;
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
    private const string RefreshEndpoint = "https://platform.claude.com/v1/oauth/token";
    private const string OAuthClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
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

        auth = await ProviderAuthFlow
            .RefreshIfNeededAsync(auth, context.Now, ShouldRefresh, RefreshAuthAsync, cancellationToken)
            .ConfigureAwait(false);

        var plan = PlanLabel(auth.SubscriptionType ?? auth.RateLimitTier);

        using var document = await ProviderAuthFlow
            .ExecuteWithRefreshRetryAsync(auth, GetUsageDocumentAsync, IsAuthFailure, HasRefreshToken, RefreshAuthAsync, cancellationToken)
            .ConfigureAwait(false);

        var session = ReadWindow(document.RootElement, "five_hour", "Session", Descriptor.Name, context.Now);
        var weekly = ReadWindow(document.RootElement, "seven_day", "Weekly", Descriptor.Name, context.Now);

        var windows = MetricWindows.Require(Descriptor.Name, session, weekly);
        return new MetricResult(Descriptor.Name, plan, windows, MetricWindows.EqualWeightBars(session, weekly));
    }

    private async Task<JsonDocument> GetUsageDocumentAsync(ClaudeAuth auth, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("anthropic-beta", BetaHeader);
        request.Headers.TryAddWithoutValidation("User-Agent", ClaudeCodeUserAgent);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Claude usage request failed with HTTP {(int)response.StatusCode}.",
                inner: null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<ClaudeAuth> RefreshAuthAsync(ClaudeAuth auth, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(auth.RefreshToken))
        {
            return auth;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, RefreshEndpoint)
        {
            Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", auth.RefreshToken),
                new KeyValuePair<string, string>("client_id", OAuthClientId),
            ]),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderException($"Claude token refresh failed with HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var accessToken = ProviderJson.GetString(root, "access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ProviderException("Claude token refresh response did not include an access token.");
        }

        var refreshed = auth with
        {
            AccessToken = accessToken,
            RefreshToken = ProviderJson.GetString(root, "refresh_token") ?? auth.RefreshToken,
            ExpiresAt = ReadExpiresAt(root),
        };

        authReader.Save(refreshed);
        return refreshed;
    }

    private static bool ShouldRefresh(ClaudeAuth auth, DateTimeOffset now)
    {
        return HasRefreshToken(auth) && (auth.ExpiresAt is null || now >= auth.ExpiresAt);
    }

    private static bool HasRefreshToken(ClaudeAuth auth) => !string.IsNullOrWhiteSpace(auth.RefreshToken);

    private static bool IsAuthFailure(HttpStatusCode? statusCode) => statusCode == HttpStatusCode.Unauthorized;

    private static DateTimeOffset? ReadExpiresAt(JsonElement root)
    {
        var expiresIn = ProviderJson.GetDouble(root, "expires_in");
        if (expiresIn is null)
        {
            return null;
        }

        return DateTimeOffset.UtcNow.AddSeconds(expiresIn.Value);
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
