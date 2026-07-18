using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UsageBar.Core.Application;
using UsageBar.Core.Providers;
using Xunit;

namespace UsageBar.Tests;

public sealed class WindowStartRequestSenderTests
{
    [Fact]
    public async Task Codex_selector_is_checked_left_to_right_and_uses_supported_responses_payload()
    {
        string? responseBody = null;
        var logger = new RecordingLogger<WindowStartRequestSender>();
        var handler = new FakeHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsoluteUri;
            if (path.Contains("registry.npmjs.org", StringComparison.Ordinal))
            {
                return JsonResponse("""{ "version": "9.9.9" }""");
            }

            if (path.Contains("/codex/models", StringComparison.Ordinal))
            {
                return JsonResponse("""
                    { "models": [
                      { "slug": "model-mini", "display_name": "Mini", "supported_reasoning_levels": [{"effort":"low"}] },
                      { "slug": "model-flash", "display_name": "Flash" },
                      { "slug": "model-large", "display_name": "Large" }
                    ] }
                    """);
            }

            responseBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: {\"type\":\"response.completed\",\"response\":{\"usage\":{\"input_tokens\":7,\"output_tokens\":1,\"total_tokens\":8}}}\ndata: [DONE]\n", Encoding.UTF8, "text/event-stream"),
            };
        });
        var sender = CreateSender(handler, logger);

        await sender.StartAsync("Codex", "flash,mini", CancellationToken.None);

        using var document = JsonDocument.Parse(responseBody!);
        Assert.Equal("model-flash", document.RootElement.GetProperty("model").GetString());
        Assert.False(document.RootElement.TryGetProperty("max_output_tokens", out _));
        Assert.Equal(".", document.RootElement.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Contains(logger.Messages, message => message.Contains("model=model-flash", StringComparison.Ordinal)
            && message.Contains("inputTokens=7", StringComparison.Ordinal)
            && message.Contains("outputTokens=1", StringComparison.Ordinal)
            && message.Contains("totalTokens=8", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Claude_uses_dynamic_haiku_match_and_one_output_token()
    {
        string? messageBody = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return JsonResponse("""
                    { "data": [
                      { "id": "claude-sonnet-current", "display_name": "Sonnet" },
                      { "id": "claude-haiku-current", "display_name": "Haiku" }
                    ] }
                    """);
            }

            messageBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse("""{ "content": [{"type":"text","text":""}] }""");
        });
        var sender = CreateSender(handler);

        await sender.StartAsync("Claude", "nano,haiku", CancellationToken.None);

        using var document = JsonDocument.Parse(messageBody!);
        Assert.Equal("claude-haiku-current", document.RootElement.GetProperty("model").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task Antigravity_fetches_project_and_models_then_uses_first_selector_match()
    {
        string? generationBody = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith(":loadCodeAssist", StringComparison.Ordinal))
            {
                return JsonResponse("""{ "cloudaicompanionProject": "dynamic-project" }""");
            }

            if (path.EndsWith(":fetchAvailableModels", StringComparison.Ordinal))
            {
                return JsonResponse("""
                    { "models": {
                      "gemini-pro-current": { "displayName": "Pro" },
                      "gemini-2.5-flash": { "displayName": "Legacy Flash" },
                      "gemini-3.5-flash-low": { "displayName": "Flash Medium", "supportsThinking": true, "recommended": true },
                      "gemini-3.5-flash-extra-low": { "displayName": "Flash Low", "supportsThinking": true, "recommended": true },
                      "internal-mini": { "displayName": "Mini", "isInternal": true }
                    } }
                    """);
            }

            generationBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: [DONE]\n", Encoding.UTF8, "text/event-stream"),
            };
        });
        var sender = CreateSender(handler);

        await sender.StartAsync("Antigravity", "mini,flash", CancellationToken.None);

        using var document = JsonDocument.Parse(generationBody!);
        Assert.Equal("gemini-3.5-flash-extra-low", document.RootElement.GetProperty("model").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("request").GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32());
        Assert.Equal(0, document.RootElement.GetProperty("request").GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingBudget").GetInt32());
    }

    private static WindowStartRequestSender CreateSender(
        HttpMessageHandler handler,
        ILogger<WindowStartRequestSender>? logger = null) =>
        new(
            new HttpClient(handler),
            new StubCodexAuthReader(new CodexAuth("codex-token", "account-id")),
            new StubClaudeAuthReader(new ClaudeAuth("claude-token")),
            new StubAntigravityAuthReader(new AntigravityAuth("antigravity-token")),
            logger ?? NullLogger<WindowStartRequestSender>.Instance);

    private static HttpResponseMessage JsonResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class StubAntigravityAuthReader(AntigravityAuth? auth) : IAntigravityAuthReader
    {
        public AntigravityAuth? Read() => auth;

        public void Save(AntigravityAuth value)
        {
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
