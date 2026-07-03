using System.Text.Json.Serialization;

namespace UsageBar.Domain;

public sealed record ProviderSettings(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("credential")] string? Credential,
    [property: JsonPropertyName("apiKey")] string? ApiKey,
    [property: JsonPropertyName("enabled")] bool Enabled)
{
    public const string TypeOAuth = "oauth";
    public const string TypeApiKey = "apiKey";
}
