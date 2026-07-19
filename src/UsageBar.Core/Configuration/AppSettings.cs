using System.Text.Json.Serialization;
using UsageBar.Core.Domain;

namespace UsageBar.Core.Configuration;

public sealed record AppSettings(
    [property: JsonPropertyName("refresh")] RefreshSettings? Refresh,
    [property: JsonPropertyName("notification")] NotificationSettings? Notification,
    [property: JsonPropertyName("visual")] VisualSettings? Visual,
    [property: JsonPropertyName("update")] UpdateSettings? Update,
    [property: JsonPropertyName("providers")] List<ProviderSettings>? Providers,
    [property: JsonPropertyName("initialized")] bool? Initialized,
    [property: JsonPropertyName("startWithSystem")] bool? StartWithSystem,
    [property: JsonPropertyName("schemaVersion")] int? SchemaVersion = 3,
    [property: JsonPropertyName("models")] ModelSettings? Models = null)
{
    public const int CurrentSchemaVersion = 3;

    public static AppSettings Default { get; } =
        new(
            Refresh: RefreshSettings.Default,
            Notification: NotificationSettings.Default,
            Visual: VisualSettings.Default,
            Update: UpdateSettings.Default,
            Providers: null,
            Initialized: false,
            StartWithSystem: true,
            SchemaVersion: CurrentSchemaVersion,
            Models: ModelSettings.Default);

    public AppSettings Normalize()
    {
        var refresh = Refresh ?? RefreshSettings.Default;
        var notification = Notification ?? NotificationSettings.Default;
        var visual = Visual ?? VisualSettings.Default;
        var update = Update ?? UpdateSettings.Default;
        var models = (Models ?? ModelSettings.Default).Normalize();

        refresh = refresh with
        {
            Minute = refresh.Minute is > 0 and <= RefreshSettings.MaxMinutes ? refresh.Minute : RefreshSettings.Default.Minute,
        };

        var high = notification.High is >= 1 and <= 100 ? notification.High : NotificationSettings.Default.High;
        var critical = notification.Critical is >= 1 and <= 100 ? notification.Critical : NotificationSettings.Default.Critical;

        if (high >= critical)
        {
            var adjusted = Math.Min(100, high + 10);
            if (adjusted > high)
            {
                critical = adjusted;
            }
            else
            {
                high = Math.Max(1, critical - 10);
            }
        }

        notification = notification with
        {
            High = high,
            Critical = critical,
            Telegram = notification.Telegram ?? TelegramSettings.Default,
            Discord = notification.Discord ?? DiscordSettings.Default,
        };

        var scale = Math.Clamp(visual.Scale ?? 100, 100, 750);

        visual = visual with
        {
            Scale = scale,
            IconLayout = (visual.IconLayout ?? TrayIconLayoutSettings.Default).Normalize(),
        };

        update = update with
        {
            OnStartup = update.OnStartup ?? true,
        };

        var providers = Providers?.Select(provider => provider with
        {
            StartWindowAfterReset = provider.StartWindowAfterReset
                ?? string.Equals(provider.Id ?? provider.Name, "codex", StringComparison.OrdinalIgnoreCase),
        }).ToList();

        return this with
        {
            Refresh = refresh,
            Notification = notification,
            Visual = visual,
            Update = update,
            Models = models,
            Providers = providers,
            Initialized = Initialized ?? (Providers is { Count: > 0 } ? true : false),
            StartWithSystem = StartWithSystem ?? true,
            SchemaVersion = CurrentSchemaVersion,
        };
    }
}
