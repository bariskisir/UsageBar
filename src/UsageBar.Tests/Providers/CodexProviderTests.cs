using System.Net;
using UsageBar.Core.Domain;
using UsageBar.Core.Providers;
using Xunit;

namespace UsageBar.Tests;

public sealed class CodexProviderTests
{
    private static CodexProvider Create(string json, CodexAuth? auth, HttpStatusCode status = HttpStatusCode.OK)
    {
        var http = new HttpClient(FakeHttpMessageHandler.Json(json, status));
        return new CodexProvider(http, new StubCodexAuthReader(auth));
    }

    [Fact]
    public async Task Parses_session_weekly_and_plan()
    {
        var primaryReset = TestData.FixedNow.AddMinutes(130).ToUnixTimeSeconds();
        var secondaryReset = TestData.FixedNow.AddDays(3).ToUnixTimeSeconds();
        var json = $$"""
        {
          "plan_type": "pro",
          "rate_limit_reset_credits": { "available_count": 1 },
          "rate_limit": {
            "primary_window": { "used_percent": 53.0, "reset_at": {{primaryReset}} },
            "secondary_window": { "used_percent": 12.5, "reset_at": {{secondaryReset}} }
          }
        }
        """;

        var result = await Create(json, new CodexAuth("token", "account")).GetUsageAsync(TestData.Context(), CancellationToken.None);

        var metric = Assert.IsType<MetricResult>(result);
        Assert.Equal("Pro", metric.Plan);
        Assert.Equal("1 reset", metric.Notice);
        Assert.Collection(
            metric.Windows,
            session =>
            {
                Assert.Equal("Session", session.Label);
                Assert.Equal(53.0, session.UsedPercent);
                Assert.Equal("2h 10m", session.ResetText);
                Assert.Null(session.SubLabel);
            },
            weekly =>
            {
                Assert.Equal("Weekly", weekly.Label);
                Assert.Equal(12.5, weekly.UsedPercent);
                Assert.Equal("3d", weekly.ResetText);
                Assert.Null(weekly.SubLabel);
            });
    }

    [Theory]
    [InlineData("0")]
    [InlineData("null")]
    public async Task Omits_available_reset_label_when_count_is_not_positive(string availableCount)
    {
        var reset = TestData.FixedNow.AddMinutes(30).ToUnixTimeSeconds();
        var json = $$"""
        {
          "rate_limit_reset_credits": { "available_count": {{availableCount}} },
          "rate_limit": {
            "primary_window": { "used_percent": 20.0, "reset_at": {{reset}} }
          }
        }
        """;

        var result = await Create(json, new CodexAuth("token", "account")).GetUsageAsync(TestData.Context(), CancellationToken.None);

        var metric = Assert.IsType<MetricResult>(result);
        Assert.Null(metric.Notice);
    }

    [Fact]
    public async Task Omits_available_reset_label_when_credits_are_absent()
    {
        var reset = TestData.FixedNow.AddMinutes(30).ToUnixTimeSeconds();
        var json = $$"""
        {
          "rate_limit": {
            "primary_window": { "used_percent": 20.0, "reset_at": {{reset}} }
          }
        }
        """;

        var result = await Create(json, new CodexAuth("token", "account")).GetUsageAsync(TestData.Context(), CancellationToken.None);

        var metric = Assert.IsType<MetricResult>(result);
        Assert.Null(metric.Notice);
    }

    [Fact]
    public async Task Pro_account_yields_two_equal_weight_bars()
    {
        var reset = TestData.FixedNow.AddMinutes(130).ToUnixTimeSeconds();
        var json = $$"""
        {
          "plan_type": "pro",
          "rate_limit": {
            "primary_window": { "used_percent": 53.0, "reset_at": {{reset}} },
            "secondary_window": { "used_percent": 12.5, "reset_at": {{reset}} }
          }
        }
        """;

        var result = await Create(json, new CodexAuth("token", "account")).GetUsageAsync(TestData.Context(), CancellationToken.None);

        var metric = Assert.IsType<MetricResult>(result);
        Assert.Collection(
            metric.Windows,
            session => Assert.Equal(53.0, session.UsedPercent),
            weekly => Assert.Equal(12.5, weekly.UsedPercent));
    }

    [Fact]
    public async Task Free_account_retains_both_usage_windows()
    {
        var reset = TestData.FixedNow.AddDays(2).ToUnixTimeSeconds();
        var json = $$"""
        {
          "plan_type": "free",
          "rate_limit": {
            "primary_window": { "used_percent": 80.0, "reset_at": {{reset}} },
            "secondary_window": { "used_percent": 25.0, "reset_at": {{reset}} }
          }
        }
        """;

        var result = await Create(json, new CodexAuth("token", "account")).GetUsageAsync(TestData.Context(), CancellationToken.None);

        var metric = Assert.IsType<MetricResult>(result);
        Assert.Equal("Free", metric.Plan);
        Assert.Collection(
            metric.Windows,
            session => Assert.Equal(80.0, session.UsedPercent),
            weekly => Assert.Equal(25.0, weekly.UsedPercent));
    }

    [Fact]
    public async Task Returns_null_when_auth_missing()
    {
        var result = await Create("{}", auth: null).GetUsageAsync(TestData.Context(), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Reads_rate_limit_from_additional_rate_limits_fallback()
    {
        var reset = TestData.FixedNow.AddMinutes(30).ToUnixTimeSeconds();
        var json = $$"""
        {
          "additional_rate_limits": [
            { "rate_limit": { "primary_window": { "used_percent": 20.0, "reset_at": {{reset}} } } }
          ]
        }
        """;

        var result = await Create(json, new CodexAuth("token", "account")).GetUsageAsync(TestData.Context(), CancellationToken.None);

        var metric = Assert.IsType<MetricResult>(result);
        var window = Assert.Single(metric.Windows);
        Assert.Equal(20.0, window.UsedPercent);
        Assert.Equal("30m", window.ResetText);
    }

    [Fact]
    public async Task Clamps_used_percent_to_100()
    {
        var reset = TestData.FixedNow.AddMinutes(5).ToUnixTimeSeconds();
        var json = $$"""
        { "rate_limit": { "primary_window": { "used_percent": 130.0, "reset_at": {{reset}} } } }
        """;

        var result = await Create(json, new CodexAuth("token", "account")).GetUsageAsync(TestData.Context(), CancellationToken.None);

        var metric = Assert.IsType<MetricResult>(result);
        Assert.Equal(100.0, Assert.Single(metric.Windows).UsedPercent);
    }

    [Fact]
    public async Task Throws_on_http_error()
    {
        var provider = Create("{}", new CodexAuth("token", "account"), HttpStatusCode.InternalServerError);
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetUsageAsync(TestData.Context(), CancellationToken.None));
    }

    [Fact]
    public async Task Refreshes_stale_token_before_usage_request()
    {
        var reset = TestData.FixedNow.AddMinutes(30).ToUnixTimeSeconds();
        var usageJson = $$"""
        {
          "plan_type": "pro",
          "rate_limit": {
            "primary_window": { "used_percent": 20.0, "reset_at": {{reset}} }
          }
        }
        """;
        var handler = FakeHttpMessageHandler.Sequence(
            ("""{ "access_token": "new-token", "refresh_token": "new-refresh", "id_token": "new-id" }""", HttpStatusCode.OK),
            (usageJson, HttpStatusCode.OK));
        var authReader = new StubCodexAuthReader(new CodexAuth(
            "old-token",
            "account",
            "old-refresh",
            LastRefresh: TestData.FixedNow.AddDays(-9)));
        var provider = new CodexProvider(new HttpClient(handler), authReader);

        var result = await provider.GetUsageAsync(TestData.Context(), CancellationToken.None);

        Assert.IsType<MetricResult>(result);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("new-token", authReader.Saved?.AccessToken);
        Assert.Equal("new-refresh", authReader.Saved?.RefreshToken);
        Assert.Equal("new-id", authReader.Saved?.IdToken);
    }

    [Fact]
    public async Task Refreshes_and_retries_once_on_auth_failure()
    {
        var reset = TestData.FixedNow.AddMinutes(30).ToUnixTimeSeconds();
        var usageJson = $$"""
        {
          "plan_type": "pro",
          "rate_limit": {
            "primary_window": { "used_percent": 20.0, "reset_at": {{reset}} }
          }
        }
        """;
        var handler = FakeHttpMessageHandler.Sequence(
            ("{}", HttpStatusCode.Unauthorized),
            ("""{ "access_token": "new-token", "refresh_token": "new-refresh" }""", HttpStatusCode.OK),
            (usageJson, HttpStatusCode.OK));
        var authReader = new StubCodexAuthReader(new CodexAuth(
            "old-token",
            "account",
            "old-refresh",
            LastRefresh: TestData.FixedNow));
        var provider = new CodexProvider(new HttpClient(handler), authReader);

        var result = await provider.GetUsageAsync(TestData.Context(), CancellationToken.None);

        Assert.IsType<MetricResult>(result);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("new-token", authReader.Saved?.AccessToken);
    }

    [Fact]
    public async Task Throws_when_no_windows_present()
    {
        var provider = Create("""{ "rate_limit": {} }""", new CodexAuth("token", "account"));
        await Assert.ThrowsAsync<ProviderException>(() => provider.GetUsageAsync(TestData.Context(), CancellationToken.None));
    }
}
