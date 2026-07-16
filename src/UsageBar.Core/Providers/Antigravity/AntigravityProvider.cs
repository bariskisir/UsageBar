using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;
public sealed class AntigravityProvider(HttpClient httpClient, IAntigravityAuthReader authReader) : ISingleResultUsageProvider
{
    private const string LoadCodeAssistEndpoint = "https://daily-cloudcode-pa.googleapis.com/v1internal:loadCodeAssist";
    private const string QuotaEndpoint = "https://daily-cloudcode-pa.googleapis.com/v1internal:retrieveUserQuotaSummary";
    private const string TokenRefreshEndpoint = "https://oauth2.googleapis.com/token";
    private const string GitHubReleasesEndpoint = "https://api.github.com/repos/google-antigravity/antigravity-cli/releases/latest";
    private const string OAuthClientId = "1071006060591-tmhssin2h21lcre235vtolojh4g403ep.apps.googleusercontent.com";
    private const string OAuthClientSecret = "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";
    private const string DefaultCliVersion = "1.0.14";
    private const string UserAgentPrefix = "antigravity/cli";
    private string? _cachedProjectId;
    private string? _cachedTierId;
    private string? _cachedCliVersion;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    public ProviderDescriptor Descriptor { get; } = new("Antigravity", 5, ProviderAuthenticationKind.OAuth, SettingsOrder: 2, IconKey: "antigravity", IconLayoutKeys: ["antigravity_*"]);

    public bool IsConfigured(ProviderQueryContext context) => !string.IsNullOrEmpty(authReader.Read()?.AccessToken);
    public async Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var auth = authReader.Read();
        if (auth is null)
        {
            return null;
        }

        var execution = await ProviderAuthFlow.ExecuteAsync(auth, context.CanRefreshToken(Descriptor.Name), context.Now, _refreshGate, authReader.Read, ShouldRefresh, static value => value.RefreshToken, RefreshAuthAsync, FetchUsageDocumentAsync, cancellationToken).ConfigureAwait(false);
        using (var document = execution.Result)
        {
            return ParseQuotaResponse(document, context);
        }
    }

    private async Task<JsonDocument> FetchUsageDocumentAsync(AntigravityAuth auth, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(auth, cancellationToken).ConfigureAwait(false);
        return await FetchQuotaAsync(auth, BuildUserAgent(), cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureInitializedAsync(AntigravityAuth auth, CancellationToken cancellationToken)
    {
        if (_cachedProjectId is not null)
        {
            return;
        }

        await _initGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedProjectId is not null)
            {
                return;
            }

            var projectTask = FetchProjectAsync(auth, cancellationToken);
            var versionTask = FetchLatestVersionAsync(cancellationToken);
            await Task.WhenAll(projectTask, versionTask).ConfigureAwait(false);
            (_cachedProjectId, _cachedTierId) = await projectTask.ConfigureAwait(false);
            _cachedCliVersion = await versionTask.ConfigureAwait(false);
        }
        finally
        {
            _initGate.Release();
        }
    }

    private MetricResult ParseQuotaResponse(JsonDocument document, ProviderQueryContext context)
    {
        var windows = new List<(int GroupIndex, UsageWindow Window)>();
        if (!ProviderJson.TryGetProperty(document.RootElement, "groups", out var groupsArray) || groupsArray.ValueKind != JsonValueKind.Array)
        {
            throw new ProviderException("Antigravity quota response did not contain groups.");
        }

        var groupIndex = 0;
        foreach (var group in groupsArray.EnumerateArray())
        {
            var groupName = ProviderJson.GetString(group, "displayName") ?? string.Empty;
            groupName = groupName.Replace("Models", "", StringComparison.OrdinalIgnoreCase).Trim();
            if (!ProviderJson.TryGetProperty(group, "buckets", out var bucketsArray) || bucketsArray.ValueKind != JsonValueKind.Array)
            {
                groupIndex++;
                continue;
            }

            foreach (var bucket in bucketsArray.EnumerateArray())
            {
                var remainingFraction = ProviderJson.GetDouble(bucket, "remainingFraction");
                if (remainingFraction is null)
                {
                    continue;
                }

                var rawWindow = ProviderJson.GetString(bucket, "window") ?? string.Empty;
                var window = MapWindowLabel(UsageFormatting.Capitalize(rawWindow));
                var usedPercent = (1.0 - remainingFraction.Value) * 100.0;
                var resetTimeStr = ProviderJson.GetString(bucket, "resetTime");
                var resetText = (string? )null;
                var resetAt = (DateTimeOffset?)null;
                if (!string.IsNullOrWhiteSpace(resetTimeStr) && DateTimeOffset.TryParse(resetTimeStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var resetTime))
                {
                    resetAt = resetTime;
                    resetText = UsageFormatting.ResetDuration(resetTime - context.Now);
                }

                var label = !string.IsNullOrWhiteSpace(window) ? window : groupName;
                windows.Add((groupIndex, new UsageWindow(Descriptor.Name, label, Math.Clamp(usedPercent, 0, 100), resetText, subLabel: !string.IsNullOrWhiteSpace(window) && !string.IsNullOrWhiteSpace(groupName) ? groupName : null, resetAt: resetAt)));
            }

            groupIndex++;
        }

        if (windows.Count == 0)
        {
            throw new ProviderException("Antigravity quota response did not contain usable buckets.");
        }

        windows.Sort((a, b) =>
        {
            var groupCmp = a.GroupIndex.CompareTo(b.GroupIndex);
            if (groupCmp != 0)
            {
                return groupCmp;
            }

            return WindowRank(a.Window.Label).CompareTo(WindowRank(b.Window.Label));
        });
        var ordered = windows.Select(w => w.Window).ToList();
        return new MetricResult(Descriptor.Name, PlanLabel(_cachedTierId), ordered);
    }

    private static string MapWindowLabel(string label)
    {
        return label switch
        {
            "5h" => "Session",
            _ => label,
        };
    }

    private static int WindowRank(string label)
    {
        return label switch
        {
            "Session" => 0,
            "Daily" => 1,
            "Weekly" => 2,
            "Monthly" => 3,
            _ => 4,
        };
    }

    private async Task<(string ProjectId, string? TierId)> FetchProjectAsync(AntigravityAuth auth, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new { metadata = new { ideType = "ANTIGRAVITY" } });
        using (var request = new HttpRequestMessage(HttpMethod.Post, LoadCodeAssistEndpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }

        )
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("User-Agent", BuildUserAgent());
            using (var document = await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false))
            {
                var root = document.RootElement;
                var projectId = ProviderJson.GetString(root, "cloudaicompanionProject");
                if (string.IsNullOrWhiteSpace(projectId))
                {
                    throw new ProviderException("Antigravity loadCodeAssist response did not contain cloudaicompanionProject.");
                }

                string? tierId = null;
                if (ProviderJson.TryGetProperty(root, "currentTier", out var currentTier) && currentTier.ValueKind == JsonValueKind.Object)
                {
                    tierId = ProviderJson.GetString(currentTier, "id");
                }

                return (projectId, tierId);
            }
        }
    }

    private async Task<string> FetchLatestVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, GitHubReleasesEndpoint))
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.TryAddWithoutValidation("User-Agent", "UsageBar");
                using (var document = await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false))
                {
                    var tagName = ProviderJson.GetString(document.RootElement, "tag_name");
                    if (!string.IsNullOrWhiteSpace(tagName))
                    {
                        return tagName.StartsWith('v') ? tagName[1..] : tagName;
                    }
                }
            }
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        // Version discovery is optional; use the last known compatible fallback.
        }

        return DefaultCliVersion;
    }

    private async Task<JsonDocument> FetchQuotaAsync(AntigravityAuth auth, string userAgent, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new { project = _cachedProjectId! });
        using (var request = new HttpRequestMessage(HttpMethod.Post, QuotaEndpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }

        )
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            return await ProviderHttp.GetJsonAsync(httpClient, request, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<AntigravityAuth> RefreshAuthAsync(AntigravityAuth auth, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(auth.RefreshToken))
        {
            return auth;
        }

        using (var request = new HttpRequestMessage(HttpMethod.Post, TokenRefreshEndpoint)
        {
            Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("client_id", OAuthClientId), new KeyValuePair<string, string>("client_secret", OAuthClientSecret), new KeyValuePair<string, string>("refresh_token", auth.RefreshToken), new KeyValuePair<string, string>("grant_type", "refresh_token"), ]),
        }

        )
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using (var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new ProviderException($"Antigravity token refresh failed with HTTP {(int)response.StatusCode}.");
                }

                await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                {
                    using (var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false))
                    {
                        var root = document.RootElement;
                        var newAccessToken = ProviderJson.GetString(root, "access_token");
                        if (string.IsNullOrWhiteSpace(newAccessToken))
                        {
                            throw new ProviderException("Antigravity token refresh response did not include an access token.");
                        }

                        var expiresIn = ProviderJson.GetDouble(root, "expires_in");
                        var expiry = expiresIn is not null ? now.AddSeconds(expiresIn.Value) : (DateTimeOffset? )null;
                        var refreshed = auth with
                        {
                            AccessToken = newAccessToken,
                            RefreshToken = ProviderJson.GetString(root, "refresh_token") ?? auth.RefreshToken,
                            Expiry = expiry,
                            IdToken = ProviderJson.GetString(root, "id_token") ?? auth.IdToken,
                        };
                        try
                        {
                            authReader.Save(refreshed);
                        }
                        catch
                        {
                        // The refreshed credential remains valid for this query even if persistence fails.
                        }

                        return refreshed;
                    }
                }
            }
        }
    }

    private static bool ShouldRefresh(AntigravityAuth auth, DateTimeOffset now) => !string.IsNullOrWhiteSpace(auth.RefreshToken) && (auth.Expiry is null || now >= auth.Expiry);
    private string BuildUserAgent() => $"{UserAgentPrefix}/{(_cachedCliVersion ?? DefaultCliVersion)} " + $"(aidev_client; os_type={GetOsType()}; arch={GetArchitecture()})";
    private static string GetOsType()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "darwin";
        }

        return "linux";
    }

    private static string GetArchitecture() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "amd64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "386",
        Architecture.Arm => "arm",
        var architecture => architecture.ToString().ToLowerInvariant(),
    };
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
