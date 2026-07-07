using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using UsageBar.Domain;

namespace UsageBar.Providers;

public sealed class AntigravityProvider(HttpClient httpClient, IAntigravityAuthReader authReader) : IUsageProvider
{
    private const string LoadCodeAssistEndpoint = "https://daily-cloudcode-pa.googleapis.com/v1internal:loadCodeAssist";
    private const string QuotaEndpoint = "https://daily-cloudcode-pa.googleapis.com/v1internal:retrieveUserQuotaSummary";
    private const string TokenRefreshEndpoint = "https://oauth2.googleapis.com/token";
    private const string GitHubReleasesEndpoint = "https://api.github.com/repos/google-antigravity/antigravity-cli/releases/latest";
    private const string OAuthClientId = "1071006060591-tmhssin2h21lcre235vtolojh4g403ep.apps.googleusercontent.com";
    private const string OAuthClientSecret = "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";
    private const string DefaultCliVersion = "1.0.14";
    private const string UserAgentPrefix = "antigravity/cli";
    private const string UserAgentSuffix = "(aidev_client; os_type=windows; arch=amd64)";

    private string? _cachedProjectId;
    private string? _cachedTierId;
    private string? _cachedCliVersion;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public ProviderDescriptor Descriptor { get; } = new("Antigravity", DisplayOrder: 5);

    public void RefreshEnabled(ProviderQueryContext context) =>
        Descriptor.IsEnabled = !string.IsNullOrEmpty(authReader.Read()?.AccessToken);

    public async Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var auth = authReader.Read();
        if (auth is null)
        {
            return null;
        }

        // Proactive token refresh if expired.
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            auth = await ProviderAuthFlow
                .RefreshIfNeededAsync(auth, context.Now, ShouldRefresh, RefreshAuthAsync, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
        }

        // One-time init: project ID + tier ID + CLI version, cached for the app lifetime.
        if (_cachedProjectId is null)
        {
            await _initGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_cachedProjectId is null)
                {
                    var projectTask = FetchProjectAsync(auth, cancellationToken);
                    var versionTask = FetchLatestVersionAsync(cancellationToken);
                    await Task.WhenAll(projectTask, versionTask).ConfigureAwait(false);

                    (_cachedProjectId, _cachedTierId) = projectTask.Result;
                    _cachedCliVersion = versionTask.Result;
                }
            }
            finally
            {
                _initGate.Release();
            }
        }

        var userAgent = BuildUserAgent();

        // Quota fetch with refresh-retry on 401.
        using var document = await ProviderAuthFlow
            .ExecuteWithRefreshRetryAsync(
                auth,
                (a, ct) => FetchQuotaAsync(a, userAgent, ct),
                IsAuthFailure,
                HasRefreshToken,
                RefreshAuthAsync,
                cancellationToken)
            .ConfigureAwait(false);

        return ParseQuotaResponse(document, context);
    }

    private MetricResult ParseQuotaResponse(JsonDocument document, ProviderQueryContext context)
    {
        var windows = new List<UsageWindow>();

        if (!ProviderJson.TryGetProperty(document.RootElement, "groups", out var groupsArray) ||
            groupsArray.ValueKind != JsonValueKind.Array)
        {
            throw new ProviderException("Antigravity quota response did not contain groups.");
        }

        foreach (var group in groupsArray.EnumerateArray())
        {
            var groupName = ProviderJson.GetString(group, "displayName") ?? string.Empty;
            groupName = groupName.Replace("Models", "", StringComparison.OrdinalIgnoreCase).Trim();

            if (!ProviderJson.TryGetProperty(group, "buckets", out var bucketsArray) ||
                bucketsArray.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var bucket in bucketsArray.EnumerateArray())
            {
                var remainingFraction = ProviderJson.GetDouble(bucket, "remainingFraction");
                if (remainingFraction is null)
                {
                    continue;
                }

                var window = UsageFormatting.Capitalize(ProviderJson.GetString(bucket, "window") ?? string.Empty);
                var usedPercent = (1.0 - remainingFraction.Value) * 100.0;
                var resetTimeStr = ProviderJson.GetString(bucket, "resetTime");

                var resetText = (string?)null;
                if (!string.IsNullOrWhiteSpace(resetTimeStr) &&
                    DateTimeOffset.TryParse(resetTimeStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var resetTime))
                {
                    resetText = UsageFormatting.ResetDuration(resetTime - context.Now);
                }

                var label = !string.IsNullOrWhiteSpace(window) ? window : groupName;

                windows.Add(new UsageWindow(Descriptor.Name, label, Math.Clamp(usedPercent, 0, 100), resetText,
                    subLabel: !string.IsNullOrWhiteSpace(window) && !string.IsNullOrWhiteSpace(groupName) ? groupName : null));
            }
        }

        if (windows.Count == 0)
        {
            throw new ProviderException("Antigravity quota response did not contain usable buckets.");
        }

        var iconBars = windows.Select(w => IconBar.Create(w.UsedPercent, 1.0)).ToList();
        return new MetricResult(Descriptor.Name, PlanLabel(_cachedTierId), windows, iconBars);
    }

    private async Task<(string ProjectId, string? TierId)> FetchProjectAsync(AntigravityAuth auth, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new { metadata = new { ideType = "ANTIGRAVITY" } });

        using var request = new HttpRequestMessage(HttpMethod.Post, LoadCodeAssistEndpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", BuildUserAgent());

        using var document = await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        var projectId = ProviderJson.GetString(root, "cloudaicompanionProject");
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new ProviderException("Antigravity loadCodeAssist response did not contain cloudaicompanionProject.");
        }

        string? tierId = null;
        if (ProviderJson.TryGetProperty(root, "currentTier", out var currentTier) &&
            currentTier.ValueKind == JsonValueKind.Object)
        {
            tierId = ProviderJson.GetString(currentTier, "id");
        }

        return (projectId, tierId);
    }

    private async Task<string> FetchLatestVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubReleasesEndpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("User-Agent", "UsageBar");

            using var document = await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false);
            var tagName = ProviderJson.GetString(document.RootElement, "tag_name");

            if (!string.IsNullOrWhiteSpace(tagName))
            {
                return tagName.StartsWith('v') ? tagName[1..] : tagName;
            }
        }
        catch { }

        return DefaultCliVersion;
    }

    private async Task<JsonDocument> FetchQuotaAsync(AntigravityAuth auth, string userAgent, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new { project = _cachedProjectId! });

        using var request = new HttpRequestMessage(HttpMethod.Post, QuotaEndpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);

        return await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AntigravityAuth> RefreshAuthAsync(AntigravityAuth auth, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(auth.RefreshToken))
        {
            return auth;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenRefreshEndpoint)
        {
            Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("client_id", OAuthClientId),
                new KeyValuePair<string, string>("client_secret", OAuthClientSecret),
                new KeyValuePair<string, string>("refresh_token", auth.RefreshToken),
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
            ]),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new ProviderException($"Antigravity token refresh failed with HTTP {(int)response.StatusCode}: {errorBody}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        var newAccessToken = ProviderJson.GetString(root, "access_token");
        if (string.IsNullOrWhiteSpace(newAccessToken))
        {
            throw new ProviderException("Antigravity token refresh response did not include an access token.");
        }

        var expiresIn = ProviderJson.GetDouble(root, "expires_in");
        var expiry = expiresIn is not null
            ? DateTimeOffset.UtcNow.AddSeconds(expiresIn.Value)
            : (DateTimeOffset?)null;

        var refreshed = auth with
        {
            AccessToken = newAccessToken,
            RefreshToken = ProviderJson.GetString(root, "refresh_token") ?? auth.RefreshToken,
            Expiry = expiry,
            IdToken = ProviderJson.GetString(root, "id_token") ?? auth.IdToken,
        };

        try { authReader.Save(refreshed); }
        catch { }

        return refreshed;
    }

    private static bool ShouldRefresh(AntigravityAuth auth, DateTimeOffset now) =>
        HasRefreshToken(auth) && (auth.Expiry is null || now >= auth.Expiry);

    private static bool HasRefreshToken(AntigravityAuth auth) =>
        !string.IsNullOrWhiteSpace(auth.RefreshToken);

    private static bool IsAuthFailure(HttpStatusCode? statusCode) =>
        statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    private string BuildUserAgent() =>
        $"{UserAgentPrefix}/{(_cachedCliVersion ?? DefaultCliVersion)} {UserAgentSuffix}";

    private static string? PlanLabel(string? tierId)
    {
        if (string.IsNullOrWhiteSpace(tierId))
        {
            return null;
        }

        var parts = tierId.Split('-', 2);
        return UsageFormatting.Capitalize(parts[0]);
    }
}
