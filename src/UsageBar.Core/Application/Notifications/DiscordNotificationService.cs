using Microsoft.Extensions.Logging;
using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;

namespace UsageBar.Core.Application;

public sealed class DiscordNotificationService : IRemoteNotificationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DiscordNotificationService> _logger;

    public DiscordNotificationService(HttpClient httpClient, ILogger<DiscordNotificationService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task SendAsync(
        string message,
        AppSettings appSettings,
        CancellationToken cancellationToken)
    {
        var discordSettings = appSettings.Notification?.Discord ?? DiscordSettings.Default;
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
                    "https://raw.githubusercontent.com/bariskisir/UsageBar/refs/heads/master/src/UsageBar.Core/Assets/AppIcon.png"),
                RemoteNotificationJsonContext.Default.DiscordWebhookPayload,
                "Discord webhook",
                "Discord",
                _logger,
                cancellationToken)
            .ConfigureAwait(false);
    }
}