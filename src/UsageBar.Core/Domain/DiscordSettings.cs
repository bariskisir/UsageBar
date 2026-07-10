using System.Text.Json.Serialization;

namespace UsageBar.Core.Domain;

public sealed record DiscordSettings(
    [property: JsonPropertyName("webhookUrl")] string? WebhookUrl,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("enabled")] bool Enabled = false)
    : IRemoteNotificationSettings
{
    public static DiscordSettings Default { get; } = new(null, "Usage Bar", false);

    [JsonIgnore]
    public bool IsEnabled => Enabled;
}
