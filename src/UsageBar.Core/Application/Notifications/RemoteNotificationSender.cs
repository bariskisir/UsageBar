using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;

namespace UsageBar.Application;

internal static class RemoteNotificationSender
{
    public static async Task PostJsonAsync<TPayload>(
        HttpClient httpClient,
        string endpoint,
        TPayload payload,
        JsonTypeInfo<TPayload> jsonTypeInfo,
        string serviceName,
        string notificationName,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload, jsonTypeInfo);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await httpClient
                .PostAsync(endpoint, content, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                logger.LogWarning("{Service} returned {StatusCode}: {Body}", serviceName, (int)response.StatusCode, body);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to send {Service} notification.", notificationName);
        }
    }
}
