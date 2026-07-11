using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using UsageBar.Core.Infrastructure.Logging;
using Xunit;

namespace UsageBar.Tests;
public sealed class UsageHttpTelemetryHandlerTests
{
    [Fact]
    public async Task Logs_request_and_response_metadata_without_secrets()
    {
        const string secret = "sentinel-http-secret";
        var logger = new RecordingLogger();
        var inner = new CapturingHandler();
        using (var telemetry = new UsageHttpTelemetryHandler(logger)
        {
            InnerHandler = inner
        }

        )
        {
            using (var client = new HttpClient(telemetry))
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.telegram.org/bot{secret}/sendMessage?chat_id={secret}")
                {
                    Content = new StringContent($"{{\"access_token\":\"{secret}\",\"status\":\"ok\"}}"),
                }

                )
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
                    using (var response = await client.SendAsync(request))
                    {
                        var responseBody = await response.Content.ReadAsStringAsync();
                        Assert.Equal("{\"status\":\"ok\"}", responseBody);
                        Assert.Equal("{\"access_token\":\"sentinel-http-secret\",\"status\":\"ok\"}", inner.RequestBody);
                        Assert.Contains(logger.Messages, message => message.Contains("HTTP request started", StringComparison.Ordinal));
                        Assert.Contains(logger.Messages, message => message.Contains("HTTP response received", StringComparison.Ordinal));
                        Assert.DoesNotContain(logger.Messages, message => message.Contains(secret, StringComparison.Ordinal));
                    }
                }
            }
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"ok\"}"),
            };
        }
    }

    private sealed class RecordingLogger : ILogger<UsageHttpTelemetryHandler>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NoopDisposable.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}