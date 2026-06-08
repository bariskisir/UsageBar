using System.Net;
using UsageBar.Domain;
using UsageBar.Providers;
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
          "rate_limit": {
            "primary_window": { "used_percent": 53.0, "reset_at": {{primaryReset}} },
            "secondary_window": { "used_percent": 12.5, "reset_at": {{secondaryReset}} }
          }
        }
        """;

        var result = await Create(json, new CodexAuth("token", "account")).GetUsageAsync(TestData.Context(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ProviderCategory.Metric, result!.Category);
        Assert.Equal("Pro", result.Plan);
        Assert.Collection(
            result.Windows,
            session =>
            {
                Assert.Equal("Session", session.Label);
                Assert.Equal(53.0, session.UsedPercent);
                Assert.Equal("2h 10m", session.ResetText);
            },
            weekly =>
            {
                Assert.Equal("Weekly", weekly.Label);
                Assert.Equal(12.5, weekly.UsedPercent);
                Assert.Equal("3d 0h", weekly.ResetText);
            });
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

        Assert.NotNull(result);
        var window = Assert.Single(result!.Windows);
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

        Assert.Equal(100.0, Assert.Single(result!.Windows).UsedPercent);
    }

    [Fact]
    public async Task Throws_on_http_error()
    {
        var provider = Create("{}", new CodexAuth("token", "account"), HttpStatusCode.InternalServerError);
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetUsageAsync(TestData.Context(), CancellationToken.None));
    }

    [Fact]
    public async Task Throws_when_no_windows_present()
    {
        var provider = Create("""{ "rate_limit": {} }""", new CodexAuth("token", "account"));
        await Assert.ThrowsAsync<ProviderException>(() => provider.GetUsageAsync(TestData.Context(), CancellationToken.None));
    }
}
