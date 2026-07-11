using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace UsageBar.Core.Application;
internal static class RemoteNotificationSender
{
    public static async Task PostJsonAsync<TPayload>(HttpClient httpClient, string endpoint, TPayload payload, JsonTypeInfo<TPayload> jsonTypeInfo, string serviceName, string notificationName, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload, jsonTypeInfo);
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            {
                using (var response = await httpClient.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        logger.LogWarning("{Service} returned HTTP {StatusCode}.", serviceName, (int)response.StatusCode);
                    }
                }
            }
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Failed to send {Service} notification: exceptionType={ExceptionType}; hresult={HResult}.", notificationName, exception.GetType().Name, exception.HResult);
        }
    }
}