using System.Net;
using UsageBar.Core.Domain;
using UsageBar.Core.Providers;
using Xunit;

namespace UsageBar.Tests;

public sealed class ClaudeProviderTests
{
    private static ClaudeProvider Create(string json, ClaudeAuth? auth, HttpStatusCode status = HttpStatusCode.OK)
    {
        var http = new HttpClient(FakeHttpMessageHandler.Json(json, status));
        return new ClaudeProvider(http, new StubClaudeAuthReader(auth));
    }

    [Fact]
    public async Task Parses_session_weekly_and_plan()
    {
        var json = """
        {
          "five_hour": { "utilization": 88.0, "resets_at": "2030-01-01T02:10:00Z" },
          "seven_day": { "utilization": 40.0, "resets_at": "2030-01-08T00:00:00Z" }
        }
        """;

        var result = await Create(json, new ClaudeAuth("token", SubscriptionType: "max"))
            .GetUsageAsync(TestData.Context(), CancellationToken.None);

        var metric = Assert.IsType<MetricResult>(result);
        Assert.Equal("Max", metric.Plan);
        Assert.Collection(
            metric.Windows,
            session =>
            {
                Assert.Equal("Session", session.Label);
                Assert.Equal(88.0, session.UsedPercent);
                Assert.Equal("2h 10m", session.ResetText);
            },
            weekly =>
            {
                Assert.Equal("Weekly", weekly.Label);
                Assert.Equal(40.0, weekly.UsedPercent);
                Assert.Equal("7d", weekly.ResetText);
            });
    }

    [Fact]
    public async Task Yields_equal_weight_session_and_weekly_bars()
    {
        var json = """
        {
          "five_hour": { "utilization": 88.0, "resets_at": "2030-01-01T02:10:00Z" },
          "seven_day": { "utilization": 40.0, "resets_at": "2030-01-08T00:00:00Z" }
        }
        """;

        var result = await Create(json, new ClaudeAuth("token", SubscriptionType: "max"))
            .GetUsageAsync(TestData.Context(), CancellationToken.None);

        var metric = Assert.IsType<MetricResult>(result);
        Assert.Collection(
            metric.Windows,
            session => Assert.Equal(88.0, session.UsedPercent),
            weekly => Assert.Equal(40.0, weekly.UsedPercent));
    }

    [Fact]
    public async Task Falls_back_to_rate_limit_tier_for_plan()
    {
        var json = """{ "five_hour": { "utilization": 10.0, "resets_at": "2030-01-01T01:00:00Z" } }""";

        var result = await Create(json, new ClaudeAuth("token", SubscriptionType: null, RateLimitTier: "claude_pro"))
            .GetUsageAsync(TestData.Context(), CancellationToken.None);

        var metric = Assert.IsType<MetricResult>(result);
        Assert.Equal("Pro", metric.Plan);
    }

    [Fact]
    public async Task Returns_null_when_auth_missing()
    {
        var result = await Create("{}", auth: null).GetUsageAsync(TestData.Context(), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Refreshes_expired_token_before_usage_request()
    {
        var usageJson = """
        {
          "five_hour": { "utilization": 25.0, "resets_at": "2030-01-01T01:00:00Z" }
        }
        """;
        var handler = FakeHttpMessageHandler.Sequence(
            ("""{ "access_token": "new-token", "refresh_token": "new-refresh", "expires_in": 3600 }""", HttpStatusCode.OK),
            (usageJson, HttpStatusCode.OK));
        var authReader = new StubClaudeAuthReader(new ClaudeAuth(
            "old-token",
            SubscriptionType: "pro",
            RefreshToken: "old-refresh",
            ExpiresAt: TestData.FixedNow.AddMinutes(-1),
            Scopes: ["user:profile"]));
        var provider = new ClaudeProvider(new HttpClient(handler), authReader);

        var result = await provider.GetUsageAsync(TestData.Context(), CancellationToken.None);

        Assert.IsType<MetricResult>(result);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("new-token", authReader.Saved?.AccessToken);
        Assert.Equal("new-refresh", authReader.Saved?.RefreshToken);
        Assert.NotNull(authReader.Saved?.ExpiresAt);
        Assert.Equal(["user:profile"], authReader.Saved?.Scopes);
    }

    [Fact]
    public async Task Refreshes_and_retries_once_on_unauthorized()
    {
        var usageJson = """
        {
          "five_hour": { "utilization": 25.0, "resets_at": "2030-01-01T01:00:00Z" }
        }
        """;
        var handler = FakeHttpMessageHandler.Sequence(
            ("{}", HttpStatusCode.Unauthorized),
            ("""{ "access_token": "new-token", "refresh_token": "new-refresh", "expires_in": 3600 }""", HttpStatusCode.OK),
            (usageJson, HttpStatusCode.OK));
        var authReader = new StubClaudeAuthReader(new ClaudeAuth(
            "old-token",
            SubscriptionType: "pro",
            RefreshToken: "old-refresh",
            ExpiresAt: TestData.FixedNow.AddMinutes(1)));
        var provider = new ClaudeProvider(new HttpClient(handler), authReader);

        var result = await provider.GetUsageAsync(TestData.Context(), CancellationToken.None);

        Assert.IsType<MetricResult>(result);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("new-token", authReader.Saved?.AccessToken);
    }

    [Fact]
    public async Task Throws_when_no_windows_present()
    {
        var provider = Create("{}", new ClaudeAuth("token"));
        await Assert.ThrowsAsync<ProviderException>(() => provider.GetUsageAsync(TestData.Context(), CancellationToken.None));
    }
}
