using UsageBar.Configuration;

namespace UsageBar.Providers;

/// <summary>
/// Per-refresh context handed to every provider. Carries the reference "now" used for
/// reset-countdown formatting and the resolved API keys (settings value first, then the
/// same-named environment variable as a fallback).
/// </summary>
public sealed class ProviderQueryContext
{
    private readonly IReadOnlyDictionary<string, string> _apiKeys;

    public ProviderQueryContext(DateTimeOffset now, IReadOnlyDictionary<string, string> apiKeys)
    {
        Now = now;
        _apiKeys = apiKeys;
    }

    /// <summary>Reference time for the current refresh (used for reset countdowns).</summary>
    public DateTimeOffset Now { get; }

    /// <summary>Returns the resolved API key for <paramref name="name"/>, or null when blank/missing.</summary>
    public string? GetApiKey(string name) =>
        _apiKeys.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>
    /// Builds a context from settings, falling back to the same-named environment
    /// variable when a settings value is blank.
    /// </summary>
    public static ProviderQueryContext FromSettings(AppSettings settings, DateTimeOffset now)
    {
        var apiKeys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CredentialNames.DeepSeek] = Resolve(settings.DeepSeekApiKey, CredentialNames.DeepSeek),
            [CredentialNames.OpenRouter] = Resolve(settings.OpenRouterApiKey, CredentialNames.OpenRouter),
            [CredentialNames.Deepgram] = Resolve(settings.DeepgramApiKey, CredentialNames.Deepgram),
            [CredentialNames.ElevenLabs] = Resolve(settings.ElevenLabsApiKey, CredentialNames.ElevenLabs),
        };

        return new ProviderQueryContext(now, apiKeys);
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
