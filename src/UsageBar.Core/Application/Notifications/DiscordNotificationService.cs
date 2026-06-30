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
        var discordSettings = _settings.Read().Discord ?? DiscordSettings.Default;
        if (!discordSettings.IsEnabled)
        {
            return;
        }

        await RemoteNotificationSender
            .PostJsonAsync(
                _httpClient,
                discordSettings.WebhookUrl!,
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
