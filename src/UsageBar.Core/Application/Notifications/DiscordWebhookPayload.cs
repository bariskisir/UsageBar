using System.Text.Json.Serialization;

namespace UsageBar.Core.Application;

internal sealed record DiscordWebhookPayload(
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("avatar_url")] string AvatarUrl);
