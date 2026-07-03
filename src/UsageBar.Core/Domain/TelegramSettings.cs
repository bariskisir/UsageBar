using System.Text.Json.Serialization;

namespace UsageBar.Domain;

public sealed record TelegramSettings(
    [property: JsonPropertyName("token")] string? Token,
    [property: JsonPropertyName("chatId")] long ChatId,
    [property: JsonPropertyName("enabled")] bool Enabled = false)
    : IRemoteNotificationSettings
{
    public static TelegramSettings Default { get; } = new(null, 0, false);

    [JsonIgnore]
    public bool IsEnabled => Enabled;
}
