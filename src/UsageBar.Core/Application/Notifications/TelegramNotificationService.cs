using Microsoft.Extensions.Logging;
using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;

namespace UsageBar.Core.Application;

public sealed class TelegramNotificationService : IRemoteNotificationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TelegramNotificationService> _logger;

    public TelegramNotificationService(HttpClient httpClient, ILogger<TelegramNotificationService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task SendAsync(
        string message,
        AppSettings appSettings,
        CancellationToken cancellationToken)
    {
        var settings = appSettings.Notification?.Telegram ?? TelegramSettings.Default;
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