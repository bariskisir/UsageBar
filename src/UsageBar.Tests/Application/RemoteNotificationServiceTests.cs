using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using UsageBar.Application;
using UsageBar.Configuration;
using UsageBar.Domain;
using Xunit;

namespace UsageBar.Tests;

public sealed class RemoteNotificationServiceTests
{
    [Fact]
    public async Task Discord_does_not_post_when_webhook_is_missing()
    {
        var handler = CaptureHandler();
        var service = new DiscordNotificationService(
            new HttpClient(handler),
            new StubSettingsStore(AppSettings.Default),
            NullLogger<DiscordNotificationService>.Instance);

        await service.SendAsync("hello", CancellationToken.None);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Discord_posts_expected_payload()
    {
        var handler = CaptureHandler();
        var service = new DiscordNotificationService(
            new HttpClient(handler),
            new StubSettingsStore(AppSettings.Default with
            {
                Discord = new DiscordSettings("https://discord.test/webhook", "Usage Bot"),
            }),
            NullLogger<DiscordNotificationService>.Instance);

        await service.SendAsync("limit reached", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://discord.test/webhook", request.RequestUri!.ToString());

        using var payload = JsonDocument.Parse(Assert.Single(handler.Bodies));
        Assert.Equal("limit reached", payload.RootElement.GetProperty("content").GetString());
        Assert.Equal("Usage Bot", payload.RootElement.GetProperty("username").GetString());
        Assert.True(payload.RootElement.TryGetProperty("avatar_url", out _));
    }

    [Fact]
    public async Task Telegram_posts_expected_payload()
    {
        var handler = CaptureHandler();
        var service = new TelegramNotificationService(
            new HttpClient(handler),
            new StubSettingsStore(AppSettings.Default with
            {
                Telegram = new TelegramSettings("token-123", 42),
            }),
            NullLogger<TelegramNotificationService>.Instance);

        await service.SendAsync("usage high", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.telegram.org/bottoken-123/sendMessage", request.RequestUri!.ToString());

        using var payload = JsonDocument.Parse(Assert.Single(handler.Bodies));
        Assert.Equal(42, payload.RootElement.GetProperty("chat_id").GetInt64());
        Assert.Equal("usage high", payload.RootElement.GetProperty("text").GetString());
        Assert.False(payload.RootElement.TryGetProperty("parse_mode", out _));
    }

    private static CapturingHttpMessageHandler CaptureHandler() =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK));

    private sealed class CapturingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return responder(request);
        }
    }
}
