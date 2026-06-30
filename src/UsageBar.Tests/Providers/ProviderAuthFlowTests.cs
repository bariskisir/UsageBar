using UsageBar.Providers;
using Xunit;

namespace UsageBar.Tests;

public sealed class ProviderAuthFlowTests
{
    [Fact]
    public async Task RefreshIfNeededAsync_calls_refresh_when_should_refresh()
    {
        var called = false;
        var auth = new Auth("initial");

        var result = await ProviderAuthFlow.RefreshIfNeededAsync(
            auth,
            TestData.FixedNow,
            (_, _) => true,
            (a, _) => { called = true; return Task.FromResult(new Auth("refreshed")); },
            CancellationToken.None);

        Assert.True(called);
        Assert.Equal("refreshed", result.Value);
    }

    [Fact]
    public async Task RefreshIfNeededAsync_skips_when_fresh()
    {
        var called = false;
        var auth = new Auth("initial");

        var result = await ProviderAuthFlow.RefreshIfNeededAsync(
            auth,
            TestData.FixedNow,
            (_, _) => false,
            (a, _) => { called = true; return Task.FromResult(new Auth("should-not-call")); },
            CancellationToken.None);

        Assert.False(called);
        Assert.Equal("initial", result.Value);
    }

    [Fact]
    public async Task ExecuteWithRefreshRetryAsync_returns_directly_on_success()
    {
        var refreshCount = 0;

        var result = await ProviderAuthFlow.ExecuteWithRefreshRetryAsync(
            new Auth("token"),
            (a, _) => Task.FromResult("success"),
            _ => false,
            _ => false,
            (a, _) => { refreshCount++; return Task.FromResult(new Auth("refreshed")); },
            CancellationToken.None);

        Assert.Equal("success", result);
        Assert.Equal(0, refreshCount);
    }

    [Fact]
    public async Task ExecuteWithRefreshRetryAsync_refreshes_and_retries_on_auth_failure()
    {
        var attempt = 0;
        var refreshCallCount = 0;

        var result = await ProviderAuthFlow.ExecuteWithRefreshRetryAsync(
            new Auth("old-token"),
            (a, _) =>
            {
                attempt++;
                if (a.Value == "old-token")
                {
                    throw new HttpRequestException("Unauthorized", inner: null, System.Net.HttpStatusCode.Unauthorized);
                }

                return Task.FromResult($"success-with-{a.Value}");
            },
            sc => sc == System.Net.HttpStatusCode.Unauthorized,
            _ => true,
            (a, _) => { refreshCallCount++; return Task.FromResult(new Auth("new-token")); },
            CancellationToken.None);

        Assert.Equal("success-with-new-token", result);
        Assert.Equal(1, refreshCallCount);
        Assert.Equal(2, attempt);
    }

    [Fact]
    public async Task ExecuteWithRefreshRetryAsync_throws_when_cannot_refresh()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            ProviderAuthFlow.ExecuteWithRefreshRetryAsync<Auth, string>(
                new Auth("token"),
                (_, _) => throw new HttpRequestException("Unauthorized", inner: null, System.Net.HttpStatusCode.Unauthorized),
                sc => sc == System.Net.HttpStatusCode.Unauthorized,
                _ => false,
                (_, _) => Task.FromResult(new Auth("ignored")),
                CancellationToken.None));
    }

    private sealed record Auth(string Value);
}
