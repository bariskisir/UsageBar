using System.Text.Json;
using System.Text.Json.Nodes;

namespace UsageBar.Providers;

/// <summary>
/// Default <see cref="IClaudeAuthReader"/> that reads
/// <c>%USERPROFILE%\.claude\.credentials.json</c> under the <c>claudeAiOauth</c> key.
/// The access token is read but never logged.
/// </summary>
public sealed class ClaudeAuthReader : IClaudeAuthReader
{
    private readonly string _authFilePath;
    private readonly object _gate = new();

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
        lock (_gate)
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
            var refreshToken = ProviderJson.GetString(oauth, "refreshToken");
            var expiresAt = ReadExpiresAt(oauth);
            var scopes = ReadScopes(oauth);

            return new ClaudeAuth(accessToken, subscriptionType, rateLimitTier, refreshToken, expiresAt, scopes);
        }
    }

    public void Save(ClaudeAuth auth)
    {
        if (string.IsNullOrWhiteSpace(_authFilePath))
        {
            return;
        }

        lock (_gate)
        {
            var root = LoadRootObject();
            var oauth = root["claudeAiOauth"] as JsonObject ?? [];
            oauth["accessToken"] = auth.AccessToken;

            if (!string.IsNullOrWhiteSpace(auth.RefreshToken))
            {
                oauth["refreshToken"] = auth.RefreshToken;
            }

            if (auth.ExpiresAt is { } expiresAt)
            {
                oauth["expiresAt"] = expiresAt.ToUnixTimeMilliseconds();
            }

            if (!string.IsNullOrWhiteSpace(auth.SubscriptionType))
            {
                oauth["subscriptionType"] = auth.SubscriptionType;
            }

            if (!string.IsNullOrWhiteSpace(auth.RateLimitTier))
            {
                oauth["rateLimitTier"] = auth.RateLimitTier;
            }

            if (auth.Scopes is { Count: > 0 })
            {
                oauth["scopes"] = new JsonArray(auth.Scopes.Select(scope => JsonValue.Create(scope)).ToArray<JsonNode?>());
            }

            root["claudeAiOauth"] = oauth;
            WriteRootObject(root);
        }
    }

    private JsonObject LoadRootObject()
    {
        if (!File.Exists(_authFilePath))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(_authFilePath)) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void WriteRootObject(JsonObject root)
    {
        var directory = Path.GetDirectoryName(_authFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var temporaryPath = _authFilePath + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _authFilePath, overwrite: true);
    }

    private static DateTimeOffset? ReadExpiresAt(JsonElement oauth)
    {
        var milliseconds = ProviderJson.GetDouble(oauth, "expiresAt");
        if (milliseconds is null)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(milliseconds.Value));
    }

    private static IReadOnlyList<string> ReadScopes(JsonElement oauth)
    {
        if (!ProviderJson.TryGetProperty(oauth, "scopes", out var scopes) || scopes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return scopes
            .EnumerateArray()
            .Where(scope => scope.ValueKind == JsonValueKind.String)
            .Select(scope => scope.GetString())
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope!.Trim())
            .ToArray();
    }
}
