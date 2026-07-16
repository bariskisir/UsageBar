using System.Text.Json.Serialization;

namespace UsageBar.Core.Domain;

public sealed record ProviderSettings(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("credential")] string? Credential,
    [property: JsonPropertyName("apiKey")] string? ApiKey,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("refreshToken")] bool RefreshToken = true,
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("startWindowAfterReset")] bool? StartWindowAfterReset = null)
{
    public const string TypeOAuth = "oauth";
    public const string TypeApiKey = "apiKey";
}
