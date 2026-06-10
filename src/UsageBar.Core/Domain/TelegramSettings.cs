using System.Text.Json.Serialization;

namespace UsageBar.Domain;

public sealed record TelegramSettings(
    [property: JsonPropertyName("token")] string? Token,
    [property: JsonPropertyName("chatId")] long ChatId)
{
    public static TelegramSettings Default { get; } = new(null, 0);

    public bool IsEnabled => !string.IsNullOrWhiteSpace(Token) && ChatId != 0;
}
