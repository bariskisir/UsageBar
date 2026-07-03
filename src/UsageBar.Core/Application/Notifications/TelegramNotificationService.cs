using Microsoft.Extensions.Logging;
using UsageBar.Domain;

namespace UsageBar.Application;

public sealed class TelegramNotificationService : IRemoteNotificationService
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsStore _settings;
    private readonly ILogger<TelegramNotificationService> _logger;

    public TelegramNotificationService(HttpClient httpClient, ISettingsStore settings, ILogger<TelegramNotificationService> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task SendAsync(
        string message,
        CancellationToken cancellationToken)
    {
        var settings = _settings.Read().Notification?.Telegram ?? TelegramSettings.Default;
        if (!settings.IsEnabled)
        {
            return;
        }

        await RemoteNotificationSender
            .PostJsonAsync(
                _httpClient,
                $"https://api.telegram.org/bot{settings.Token}/sendMessage",
                new TelegramMessagePayload(settings.ChatId, message, null),
                RemoteNotificationJsonContext.Default.TelegramMessagePayload,
                "Telegram API",
                "Telegram",
                _logger,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
