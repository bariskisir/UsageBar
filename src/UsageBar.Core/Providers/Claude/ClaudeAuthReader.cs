using System.Text.Json;

namespace UsageBar.Providers;

/// <summary>
/// Default <see cref="IClaudeAuthReader"/> that reads
/// <c>%USERPROFILE%\.claude\.credentials.json</c> under the <c>claudeAiOauth</c> key.
/// The access token is read but never logged.
/// </summary>
public sealed class ClaudeAuthReader : IClaudeAuthReader
{
    private readonly string _authFilePath;

    public ClaudeAuthReader()
        : this(DefaultAuthFilePath())
    {
    }

    public ClaudeAuthReader(string authFilePath) => _authFilePath = authFilePath;

    /// <summary>The conventional Claude credentials file location for the current user.</summary>
    public static string DefaultAuthFilePath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".claude", ".credentials.json");
    }

    public ClaudeAuth? Read()
    {
        if (string.IsNullOrWhiteSpace(_authFilePath) || !File.Exists(_authFilePath))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(_authFilePath));
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

        var subscriptionType = ProviderJson.GetString(oauth, "subscriptionType");
        var rateLimitTier = ProviderJson.GetString(oauth, "rateLimitTier");

        return new ClaudeAuth(accessToken, subscriptionType, rateLimitTier);
    }
}
