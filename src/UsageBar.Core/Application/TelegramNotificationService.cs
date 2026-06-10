using System.Text;
using System.Text.Json;
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
        NotificationLevel level,
        string message,
        CancellationToken cancellationToken)
    {
        var settings = _settings.Read().Telegram ?? TelegramSettings.Default;
        if (!settings.IsEnabled)
        {
            return;
        }

        try
        {
            var emoji = level switch
            {
                NotificationLevel.Critical => "\u26a0\ufe0f ",
                NotificationLevel.High => "\u26a1 ",
                NotificationLevel.Reset => "\u2705 ",
                _ => string.Empty,
            };

            var text = $"{emoji}{message}";

            var payload = JsonSerializer.Serialize(new
            {
                chat_id = settings.ChatId,
                text,
                parse_mode = "Markdown",
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _httpClient
                .PostAsync($"https://api.telegram.org/bot{settings.Token}/sendMessage", content, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Telegram API returned {StatusCode}: {Body}", (int)response.StatusCode, body);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to send Telegram notification.");
        }
    }
}
