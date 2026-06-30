using System.Text.Json.Serialization;

namespace UsageBar.Application;

[JsonSerializable(typeof(DiscordWebhookPayload))]
[JsonSerializable(typeof(TelegramMessagePayload))]
internal sealed partial class RemoteNotificationJsonContext : JsonSerializerContext;
