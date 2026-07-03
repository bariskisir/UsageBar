using System.Text.Json.Serialization;

namespace UsageBar.Domain;

public sealed record NotificationSettings(
    [property: JsonPropertyName("high")] double High,
    [property: JsonPropertyName("critical")] double Critical,
    [property: JsonPropertyName("telegram")] TelegramSettings? Telegram,
    [property: JsonPropertyName("discord")] DiscordSettings? Discord)
{
    public static NotificationSettings Default { get; } = new(70, 90, null, null);
}
