using System.Text.Json.Serialization;

namespace UsageBar.Providers;

internal sealed record CodexRefreshTokenRequest(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("grant_type")] string GrantType,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("scope")] string Scope);
