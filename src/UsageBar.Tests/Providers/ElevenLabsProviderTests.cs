using UsageBar.Core.Domain;
using UsageBar.Core.Providers;
using Xunit;

namespace UsageBar.Tests;

public sealed class ElevenLabsProviderTests
{
    [Fact]
    public void Sorts_after_session_weekly_providers_and_before_balance_providers()
    {
        var provider = new ElevenLabsProvider(new HttpClient(FakeHttpMessageHandler.Json("{}")));

        Assert.Equal(20, provider.Descriptor.DisplayOrder);
    }

    [Fact]
    public async Task Reports_usage_percentage_from_character_counts()
    {
        var resetAt = TestData.FixedNow.AddDays(12);
        var json = $$"""
        {
          "tier": "free",
          "character_count": 738,
          "character_limit": 10000,
          "next_character_count_reset_unix": {{resetAt.ToUnixTimeSeconds()}}
        }
        """;
        var handler = FakeHttpMessageHandler.Json(json);
        var provider = new ElevenLabsProvider(new HttpClient(handler));

        var result = await provider.GetUsageAsync(
            TestData.Context((CredentialNames.ElevenLabs, "eleven-key")),
            CancellationToken.None);

        var metric = Assert.IsType<MetricResult>(result);
        Assert.Equal("ElevenLabs", metric.ProviderName);
        Assert.Equal("Free", metric.Plan);
        var window = Assert.Single(metric.Windows);
        Assert.Equal("Session", window.Label);
        Assert.Equal(7.38, window.UsedPercent, precision: 2);
        Assert.Equal("12d", window.ResetText);
        Assert.Contains("eleven-key", handler.Requests[0].Headers.GetValues("xi-api-key"));
        Assert.Equal("https://api.elevenlabs.io/v1/user/subscription", handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Clamps_usage_percentage_when_over_limit()
    {
        var resetAt = TestData.FixedNow.AddHours(18);
        var json = $$"""
        {
          "tier": "free",
          "character_count": 120000,
          "character_limit": 100000,
          "next_character_count_reset_unix": {{resetAt.ToUnixTimeSeconds()}}
        }
        """;
        var provider = new ElevenLabsProvider(new HttpClient(FakeHttpMessageHandler.Json(json)));

        var result = await provider.GetUsageAsync(
            TestData.Context((CredentialNames.ElevenLabs, "eleven-key")),
            CancellationToken.None);

        var metric = Assert.IsType<MetricResult>(result);
        var window = Assert.Single(metric.Windows);
        Assert.Equal("Session", window.Label);
        Assert.Equal(100, window.UsedPercent);
    }

    [Fact]
    public async Task Returns_null_without_api_key()
    {
        var provider = new ElevenLabsProvider(new HttpClient(FakeHttpMessageHandler.Json("{}")));

        var result = await provider.GetUsageAsync(TestData.Context(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Throws_when_required_fields_are_missing()
    {
        var provider = new ElevenLabsProvider(new HttpClient(FakeHttpMessageHandler.Json("{}")));

        await Assert.ThrowsAsync<ProviderException>(
            () => provider.GetUsageAsync(TestData.Context((CredentialNames.ElevenLabs, "eleven-key")), CancellationToken.None));
    }
}