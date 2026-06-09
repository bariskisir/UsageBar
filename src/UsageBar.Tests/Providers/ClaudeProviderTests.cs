using System.Net;
using UsageBar.Domain;
using UsageBar.Providers;
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
                Assert.Equal("7d 0h", weekly.ResetText);
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
            metric.IconBars,
            session => { Assert.Equal(88.0, session.UsedPercent); Assert.Equal(1.0, session.Weight); },
            weekly => { Assert.Equal(40.0, weekly.UsedPercent); Assert.Equal(1.0, weekly.Weight); });
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
    public async Task Throws_when_no_windows_present()
    {
        var provider = Create("{}", new ClaudeAuth("token"));
        await Assert.ThrowsAsync<ProviderException>(() => provider.GetUsageAsync(TestData.Context(), CancellationToken.None));
    }
}
