using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>
/// Reports Codex (ChatGPT) rate-limit usage as Session (5h) and Weekly (7d) windows,
/// plus the account plan/tier. Credentials come from the local Codex CLI auth file.
/// </summary>
public sealed class CodexProvider(HttpClient httpClient, ICodexAuthReader authReader) : IUsageProvider
{
    private const string UsageEndpoint = "https://chatgpt.com/backend-api/wham/usage";
    private const string RefreshEndpoint = "https://auth.openai.com/oauth/token";
    private const string OAuthClientId = "app_EMoamEEZ73f0CkXaXp7hrann";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(8);

    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public ProviderDescriptor Descriptor { get; } = new(
        "Codex", 0, ProviderAuthenticationKind.OAuth, SettingsOrder: 0, IconKey: "openai",
        IconLayoutKeys: ["codex_session", "codex_weekly"]);

    public bool IsConfigured(ProviderQueryContext context) =>
        !string.IsNullOrEmpty(authReader.Read()?.AccessToken);

    public async Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var auth = authReader.Read();
        if (auth is null)
        {
            return null;
        }

        var execution = await ProviderAuthFlow.ExecuteAsync(
                auth,
                context.CanRefreshToken(Descriptor.Name),
                context.Now,
                _refreshGate,
                authReader.Read,
                ShouldRefresh,
                static value => value.RefreshToken,
                RefreshAuthAsync,
                GetUsageDocumentAsync,
                cancellationToken)
            .ConfigureAwait(false);
        using var document = execution.Result;

        var plan = PlanLabel(ProviderJson.GetString(document.RootElement, "plan_type"));
        var rateLimit = GetRateLimit(document.RootElement);

        var session = ReadWindow(rateLimit, "primary_window", "Session", Descriptor.Name, context.Now);
        var weekly = ReadWindow(rateLimit, "secondary_window", "Weekly", Descriptor.Name, context.Now);

        var windows = MetricWindows.Require(Descriptor.Name, session, weekly);
        return new MetricResult(Descriptor.Name, plan, windows);
    }

    private async Task<JsonDocument> GetUsageDocumentAsync(CodexAuth auth, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        request.Headers.TryAddWithoutValidation("User-Agent", "UsageBar");
        request.Headers.TryAddWithoutValidation("originator", "codex_cli_rs");

        if (!string.IsNullOrWhiteSpace(auth.AccountId))
        {
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", auth.AccountId);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Codex usage request failed with HTTP {(int)response.StatusCode}.",
                inner: null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<CodexAuth> RefreshAuthAsync(
        CodexAuth auth,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(auth.RefreshToken))
        {
            return auth;
        }

        var body = JsonSerializer.Serialize(
            new CodexRefreshTokenRequest(
                OAuthClientId,
                "refresh_token",
                auth.RefreshToken,
                "openid profile email"),
            CodexJsonContext.Default.CodexRefreshTokenRequest);

        using var request = new HttpRequestMessage(HttpMethod.Post, RefreshEndpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderException($"Codex token refresh failed with HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var refreshed = auth with
        {
            AccessToken = ProviderJson.GetString(root, "access_token") ?? auth.AccessToken,
            RefreshToken = ProviderJson.GetString(root, "refresh_token") ?? auth.RefreshToken,
            IdToken = ProviderJson.GetString(root, "id_token") ?? auth.IdToken,
            LastRefresh = now,
        };

        try
        {
            authReader.Save(refreshed);
        }
        catch
        {
            // Persisting the new credentials failed (disk full, permissions, etc.).
            // The in-memory auth is still valid for this refresh cycle; on the next
            // cycle the old token will be read from disk and re-refreshed if expired.
        }

        return refreshed;
    }

    private static bool ShouldRefresh(CodexAuth auth, DateTimeOffset now)
    {
        return !string.IsNullOrWhiteSpace(auth.RefreshToken) &&
            (auth.LastRefresh is null || now - auth.LastRefresh > RefreshInterval);
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
        // Truncate toward zero (standard epoch conversion) instead of banker's rounding.
        return DateTimeOffset.FromUnixTimeSeconds((long)seconds);
    }
}
