using System.Text.Json.Serialization;

namespace UsageBar.Application;

internal sealed record TelegramMessagePayload(
    [property: JsonPropertyName("chat_id")] long ChatId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("parse_mode")] string ParseMode);
