using UsageBar.Configuration;
using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>
/// Per-refresh context handed to every provider. Carries the reference "now" used for
/// reset-countdown formatting and the resolved API keys (settings value first, then the
/// same-named environment variable as a fallback).
/// </summary>
public sealed class ProviderQueryContext
{
    private readonly IReadOnlyDictionary<string, string> _apiKeys;
    private readonly IReadOnlyDictionary<string, bool> _refreshTokenMap;

    public ProviderQueryContext(
        DateTimeOffset now,
        IReadOnlyDictionary<string, string> apiKeys,
        IReadOnlyDictionary<string, bool>? refreshTokenMap = null)
    {
        Now = now;
        _apiKeys = apiKeys;
        _refreshTokenMap = refreshTokenMap ?? new Dictionary<string, bool>();
    }

    /// <summary>Reference time for the current refresh (used for reset countdowns).</summary>
    public DateTimeOffset Now { get; }

    /// <summary>Returns the resolved API key for <paramref name="name"/>, or null when blank/missing.</summary>
    public string? GetApiKey(string name) =>
        _apiKeys.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>Returns whether the named provider is allowed to refresh its OAuth token.</summary>
    public bool CanRefreshToken(string providerName) =>
        _refreshTokenMap.TryGetValue(providerName, out var canRefresh) ? canRefresh : true;

    /// <summary>
    /// Builds a context from settings, falling back to the same-named environment
    /// variable when a settings value is blank.
    /// </summary>
    public static ProviderQueryContext FromSettings(AppSettings settings, DateTimeOffset now)
    {
        var apiKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var refreshTokenMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        if (settings.Providers is { Count: > 0 })
        {
            foreach (var p in settings.Providers)
            {
                refreshTokenMap[p.Name] = p.RefreshToken;

                if (p.Type != ProviderSettings.TypeApiKey || string.IsNullOrEmpty(p.Credential))
                {
                    continue;
                }

                apiKeys[p.Credential] = Resolve(p.ApiKey, p.Credential);
            }
        }

        return new ProviderQueryContext(now, apiKeys, refreshTokenMap);
    }

    private static string Resolve(string? settingsValue, string environmentVariableName)
    {
        if (!string.IsNullOrWhiteSpace(settingsValue))
        {
            return settingsValue;
        }

        return Environment.GetEnvironmentVariable(environmentVariableName) ?? string.Empty;
    }
}
