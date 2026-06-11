namespace UsageBar.Providers;

/// <summary>Codex OAuth material required to query usage.</summary>
public sealed record CodexAuth(
    string AccessToken,
    string? AccountId = null,
    string? RefreshToken = null,
    string? IdToken = null,
    DateTimeOffset? LastRefresh = null);

/// <summary>Reads Codex OAuth credentials from the local Codex CLI auth file.</summary>
public interface ICodexAuthReader
{
    /// <summary>Returns the current auth, or <see langword="null"/> when unavailable/incomplete.</summary>
    CodexAuth? Read();

    /// <summary>Persists refreshed OAuth material back to the local Codex auth file.</summary>
    void Save(CodexAuth auth);
}
