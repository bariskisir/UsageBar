using System.Text.Json.Serialization;

namespace UsageBar.Domain;

public sealed record DiscordSettings(
    [property: JsonPropertyName("webhookUrl")] string? WebhookUrl,
    [property: JsonPropertyName("username")] string? Username)
{
    public static DiscordSettings Default { get; } = new(null, "Usage Bar");

    [JsonIgnore]
    public bool IsEnabled => !string.IsNullOrWhiteSpace(WebhookUrl);

    /// <summary>Redacts the webhook URL so this record is safe to log.</summary>
    public override string ToString() =>
        $"DiscordSettings {{ WebhookUrl = ***, Username = {Username ?? "null"} }}";
}
