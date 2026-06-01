using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using UsageBar.Domain;

namespace UsageBar.Providers;

internal sealed class ClaudeProvider(HttpClient httpClient) : IUsageProvider
{
    private const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";
    private const string BetaHeader = "oauth-2025-04-20";
    private const string ClaudeCodeUserAgent = "claude-code/2.1.0";

    public string Name => "Claude";

    public async Task<ProviderResult?> GetUsageAsync(ProviderCredentials credentials, CancellationToken cancellationToken)
    {
        var auth = ReadAuth();
        if (auth is null)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("anthropic-beta", BetaHeader);
        request.Headers.TryAddWithoutValidation("User-Agent", ClaudeCodeUserAgent);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var fiveHour = GetWindow(document.RootElement, "five_hour");
        var sevenDay = GetWindow(document.RootElement, "seven_day");

        var blocks = new List<UsageBlock>();
        var windows = new List<UsageBarWindow>();

        if (fiveHour is not null)
        {
            blocks.Add(new UsageBlock("Claude 5h:", FormatUsageLine(fiveHour.Value), Inline: true));
            windows.Add(new UsageBarWindow("Claude", "5h", Math.Clamp(fiveHour.Value.UsedPercent, 0, 100)));
        }

        if (sevenDay is not null)
        {
            blocks.Add(new UsageBlock("Claude 7d:", FormatUsageLine(sevenDay.Value), Inline: true));
            windows.Add(new UsageBarWindow("Claude", "7d", Math.Clamp(sevenDay.Value.UsedPercent, 0, 100)));
        }

        if (blocks.Count == 0)
        {
            throw new InvalidOperationException("Claude response did not contain usable rate limit windows.");
        }

        return new ProviderResult(blocks, windows);
    }

    private static ClaudeAuth? ReadAuth()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            return null;
        }

        var authPath = Path.Combine(userProfile, ".claude", ".credentials.json");
        if (!File.Exists(authPath))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(authPath));
        var root = document.RootElement;

        if (!ProviderJson.TryGetProperty(root, "claudeAiOauth", out var oauth) || oauth.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var accessToken = ProviderJson.GetString(oauth, "accessToken");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        return new ClaudeAuth(accessToken);
    }

    private static ClaudeWindow? GetWindow(JsonElement root, string propertyName)
    {
        if (!ProviderJson.TryGetProperty(root, propertyName, out var window) || window.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var utilization = ProviderJson.GetDouble(window, "utilization");
        var resetsAt = ProviderJson.GetString(window, "resets_at");

        if (utilization is null)
        {
            return null;
        }

        var usedPercent = utilization.Value;
        var resetTime = ParseResetTime(resetsAt);

        return new ClaudeWindow(usedPercent, resetTime);
    }

    private static DateTimeOffset? ParseResetTime(string? iso8601)
    {
        if (string.IsNullOrWhiteSpace(iso8601))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(iso8601, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string FormatUsageLine(ClaudeWindow window)
    {
        var percent = string.Create(CultureInfo.InvariantCulture, $"{window.UsedPercent:0.#}%");
        if (window.ResetAt is DateTimeOffset reset)
        {
            var duration = reset - DateTimeOffset.Now;
            return $"{percent}, {FormatResetDuration(duration)}";
        }

        return percent;
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

    private sealed record ClaudeAuth(string AccessToken);

    private readonly record struct ClaudeWindow(double UsedPercent, DateTimeOffset? ResetAt);
}
