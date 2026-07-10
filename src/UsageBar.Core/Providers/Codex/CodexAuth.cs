namespace UsageBar.Core.Providers;

/// <summary>Codex OAuth material required to query usage.</summary>
public sealed record CodexAuth(
    string AccessToken,
    string? AccountId = null,
    string? RefreshToken = null,
    string? IdToken = null,
    DateTimeOffset? LastRefresh = null);
