using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace UsageBar.Core.Infrastructure.Logging;
internal sealed class UsageHttpTelemetryHandler(ILogger<UsageHttpTelemetryHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N")[..12];
        var started = Stopwatch.GetTimestamp();
        var safeUri = SensitiveDataRedactor.SafeUri(request.RequestUri);
        var requestLength = request.Content?.Headers.ContentLength;
        using (var scope = logger.BeginScope(new Dictionary<string, object?> { ["HttpRequestId"] = requestId, ["HttpMethod"] = request.Method.Method, ["HttpUri"] = safeUri, }))
        {
            logger.LogInformation("HTTP request started: {Method} {Uri}; headers={Headers}; contentType={ContentType}; contentLength={ContentLength}.", request.Method.Method, safeUri, SensitiveDataRedactor.HeaderNames(request.Headers, request.Content?.Headers), request.Content?.Headers.ContentType?.MediaType, requestLength);
            await LogBodyAsync("request", request.Content, cancellationToken).ConfigureAwait(false);
            try
            {
                var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var elapsed = Stopwatch.GetElapsedTime(started);
                logger.LogInformation("HTTP response received: status={StatusCode}; success={Success}; durationMs={DurationMs:F1}; headers={Headers}; contentType={ContentType}; contentLength={ContentLength}.", (int)response.StatusCode, response.IsSuccessStatusCode, elapsed.TotalMilliseconds, SensitiveDataRedactor.HeaderNames(response.Headers, response.Content.Headers), response.Content.Headers.ContentType?.MediaType, response.Content.Headers.ContentLength);
                await LogBodyAsync("response", response.Content, cancellationToken).ConfigureAwait(false);
                return response;
            }
            catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("HTTP request cancelled by caller after {DurationMs:F1} ms.", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                throw;
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("HTTP request timed out after {DurationMs:F1} ms.", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                throw;
            }
            catch (HttpRequestException exception)
            {
                logger.LogWarning("HTTP transport failure after {DurationMs:F1} ms: type={ExceptionType}; status={StatusCode}; hresult={HResult}.", Stopwatch.GetElapsedTime(started).TotalMilliseconds, exception.GetType().Name, exception.StatusCode is null ? null : (int)exception.StatusCode, exception.HResult);
                throw;
            }
        }
    }

    private async Task LogBodyAsync(string direction, HttpContent? content, CancellationToken cancellationToken)
    {
        if (content is null || !logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        if (!LogConfiguration.IsHttpBodyLoggingEnabled)
        {
            logger.LogDebug("HTTP {Direction} body omitted; enable USAGEBAR_HTTP_BODY_LOGGING for a redacted diagnostic snapshot.", direction);
            return;
        }

        var declaredLength = content.Headers.ContentLength;
        if (declaredLength is> 1_000_000)
        {
            logger.LogDebug("HTTP {Direction} body skipped because declared length is {Length} bytes.", direction, declaredLength);
            return;
        }

        try
        {
            var body = await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            logger.LogDebug("HTTP {Direction} body: fingerprint={Fingerprint}; snapshot={BodySnapshot}", direction, SensitiveDataRedactor.BodyFingerprint(body), SensitiveDataRedactor.BodySnapshot(body, content.Headers.ContentType?.MediaType));
        }
        catch (Exception exception)when (exception is not OperationCanceledException)
        {
            logger.LogDebug("HTTP {Direction} body could not be inspected: {ExceptionType}.", direction, exception.GetType().Name);
        }
    }
}