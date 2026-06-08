using System.Text.Json;

namespace UsageBar.Providers;

/// <summary>
/// Default <see cref="ICodexAuthReader"/> that reads
/// <c>%USERPROFILE%\.codex\auth.json</c>. The access token and account id are read but
/// never logged.
/// </summary>
public sealed class CodexAuthReader : ICodexAuthReader
{
    private readonly string _authFilePath;

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
        if (string.IsNullOrWhiteSpace(_authFilePath) || !File.Exists(_authFilePath))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(_authFilePath));
        var root = document.RootElement;

        var tokenSource = ProviderJson.TryGetProperty(root, "tokens", out var tokens) ? tokens : root;
        var accessToken = ProviderJson.GetString(tokenSource, "access_token");
        var accountId = ProviderJson.GetString(tokenSource, "account_id");

        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(accountId))
        {
            return null;
        }

        return new CodexAuth(accessToken, accountId);
    }
}
