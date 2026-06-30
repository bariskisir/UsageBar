using System.Text.Json.Serialization;

namespace UsageBar.Domain;

public sealed record TelegramSettings(
    [property: JsonPropertyName("token")] string? Token,
    [property: JsonPropertyName("chatId")] long ChatId)
{
    public static TelegramSettings Default { get; } = new(null, 0);

    [JsonIgnore]
    public bool IsEnabled => !string.IsNullOrWhiteSpace(Token) && ChatId != 0;

    /// <summary>Redacts the bot token so this record is safe to log.</summary>
    public override string ToString() =>
        $"TelegramSettings {{ Token = ***, ChatId = {ChatId} }}";
}
