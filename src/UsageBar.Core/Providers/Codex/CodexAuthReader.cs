using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UsageBar.Providers;

/// <summary>
/// Default <see cref="ICodexAuthReader"/> that reads
/// <c>%USERPROFILE%\.codex\auth.json</c>. The access token and account id are read but
/// never logged.
/// </summary>
public sealed class CodexAuthReader : ICodexAuthReader
{
    private readonly string _authFilePath;
    private readonly Lock _gate = new();

    public CodexAuthReader()
        : this(DefaultAuthFilePath())
    {
    }

    public CodexAuthReader(string authFilePath) => _authFilePath = authFilePath;

    /// <summary>The conventional Codex CLI auth file location for the current user.</summary>
    public static string DefaultAuthFilePath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".codex", "auth.json");
    }

    public CodexAuth? Read()
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(_authFilePath) || !File.Exists(_authFilePath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(_authFilePath));
            var root = document.RootElement;

            var tokenSource = ProviderJson.TryGetProperty(root, "tokens", out var tokens) ? tokens : root;
            var accessToken = ProviderJson.GetString(tokenSource, "access_token");
            var accountId = ProviderJson.GetString(tokenSource, "account_id");
            var refreshToken = ProviderJson.GetString(tokenSource, "refresh_token");
            var idToken = ProviderJson.GetString(tokenSource, "id_token");
            var lastRefresh = ParseDateTimeOffset(ProviderJson.GetString(root, "last_refresh"));

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return null;
            }

            return new CodexAuth(accessToken, accountId, refreshToken, idToken, lastRefresh);
        }
    }

    public void Save(CodexAuth auth)
    {
        if (string.IsNullOrWhiteSpace(_authFilePath))
        {
            return;
        }

        lock (_gate)
        {
            var root = LoadRootObject();
            var tokens = root["tokens"] as JsonObject ?? [];
            tokens["access_token"] = auth.AccessToken;

            if (!string.IsNullOrWhiteSpace(auth.RefreshToken))
            {
                tokens["refresh_token"] = auth.RefreshToken;
            }

            if (!string.IsNullOrWhiteSpace(auth.IdToken))
            {
                tokens["id_token"] = auth.IdToken;
            }

            if (!string.IsNullOrWhiteSpace(auth.AccountId))
            {
                tokens["account_id"] = auth.AccountId;
            }

            root["tokens"] = tokens;
            root["last_refresh"] = FormatLastRefresh(auth.LastRefresh ?? DateTimeOffset.UtcNow);
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

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatLastRefresh(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        var fractionalTicks = utc.Ticks % TimeSpan.TicksPerSecond;
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:yyyy-MM-dd'T'HH:mm:ss}.{1:D7}Z",
            utc,
            fractionalTicks);
    }
}
