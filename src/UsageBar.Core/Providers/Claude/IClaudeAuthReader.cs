namespace UsageBar.Providers;

/// <summary>Claude OAuth material required to query usage.</summary>
public sealed record ClaudeAuth(
    string AccessToken,
    string? SubscriptionType = null,
    string? RateLimitTier = null,
    string? RefreshToken = null,
    DateTimeOffset? ExpiresAt = null,
    IReadOnlyList<string>? Scopes = null);

/// <summary>Reads Claude OAuth credentials from the local Claude credentials file.</summary>
public interface IClaudeAuthReader
{
    /// <summary>Returns the current auth, or <see langword="null"/> when unavailable/incomplete.</summary>
    ClaudeAuth? Read();

    /// <summary>Persists refreshed OAuth material back to the local Claude credentials file.</summary>
    void Save(ClaudeAuth auth);
}
