using Microsoft.Extensions.Logging;
using UsageBar.Domain;

namespace UsageBar.Application;

public sealed class DiscordNotificationService : IRemoteNotificationService
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsStore _settings;
    private readonly ILogger<DiscordNotificationService> _logger;

    public DiscordNotificationService(HttpClient httpClient, ISettingsStore settings, ILogger<DiscordNotificationService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task SendAsync(
        string message,
        CancellationToken cancellationToken)
    {
        var discordSettings = _settings.Read().Notification?.Discord ?? DiscordSettings.Default;
        if (!discordSettings.IsEnabled)
        {
            return;
        }

        // IsEnabled guarantees WebhookUrl is non-null and non-whitespace.
        var webhookUrl = discordSettings.WebhookUrl!;

        await RemoteNotificationSender
            .PostJsonAsync(
                _httpClient,
                webhookUrl,
                new DiscordWebhookPayload(
                    message,
                    discordSettings.Username ?? "Usage Bar",
                    "https://raw.githubusercontent.com/bariskisir/UsageBar/refs/heads/master/src/UsageBar.App/Assets/AppIcon.png"),
                RemoteNotificationJsonContext.Default.DiscordWebhookPayload,
                "Discord webhook",
                "Discord",
                _logger,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
