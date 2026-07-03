using System.Text.Json.Serialization;
using UsageBar.Configuration;
using UsageBar.Domain;

namespace UsageBar.Infrastructure;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(RefreshSettings))]
[JsonSerializable(typeof(NotificationSettings))]
[JsonSerializable(typeof(VisualSettings))]
[JsonSerializable(typeof(UpdateSettings))]
[JsonSerializable(typeof(ProviderSettings))]
[JsonSerializable(typeof(TelegramSettings))]
[JsonSerializable(typeof(DiscordSettings))]
[JsonSerializable(typeof(TrayIconLayoutSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
