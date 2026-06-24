using System.Text;
using System.Text.Json;
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

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                content = message,
                username = discordSettings.Username ?? "Usage Bar",
                avatar_url = "https://raw.githubusercontent.com/bariskisir/UsageBar/refs/heads/master/src/UsageBar.App/Assets/AppIcon.png",
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _httpClient
                .PostAsync(discordSettings.WebhookUrl, content, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Discord webhook returned {StatusCode}: {Body}", (int)response.StatusCode, body);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to send Discord notification.");
        }
    }
}
