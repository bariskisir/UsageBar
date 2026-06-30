namespace UsageBar.Providers;

/// <summary>Claude OAuth material required to query usage.</summary>
public sealed record ClaudeAuth(
    string AccessToken,
    string? SubscriptionType = null,
    string? RateLimitTier = null,
    string? RefreshToken = null,
    DateTimeOffset? ExpiresAt = null,
    IReadOnlyList<string>? Scopes = null);
