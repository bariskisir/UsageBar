namespace UsageBar.Core.Providers;

/// <summary>Antigravity (Gemini Code Assist) OAuth material required to query usage.</summary>
public sealed record AntigravityAuth(
    string AccessToken,
    string? RefreshToken = null,
    DateTimeOffset? Expiry = null,
    string? IdToken = null);