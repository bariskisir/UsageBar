using System.Net;
using UsageBar.Core.Providers;
using Xunit;

namespace UsageBar.Tests;

public sealed class ProviderAuthFlowTests
{
    [Fact]
    public async Task ExecuteAsync_refreshes_stale_auth_before_execution()
    {
        var refreshCount = 0;
        var gate = new SemaphoreSlim(1, 1);
        var auth = new Auth("initial", "refresh-token");

        var execution = await ProviderAuthFlow.ExecuteAsync(
            auth,
            allowRefresh: true,
            TestData.FixedNow,
            gate,
            () => auth,
            (_, _) => true,
            value => value.RefreshToken,
            (value, _, _) =>
            {
                refreshCount++;
                return Task.FromResult(value with { Value = "refreshed" });
            },
            (value, _) => Task.FromResult($"success-with-{value.Value}"),
            CancellationToken.None);

        Assert.Equal(1, refreshCount);
        Assert.Equal("refreshed", execution.Auth.Value);
        Assert.Equal("success-with-refreshed", execution.Result);
    }

    [Fact]
    public async Task ExecuteAsync_skips_refresh_when_auth_is_fresh()
    {
        var refreshCount = 0;
        var auth = new Auth("initial", "refresh-token");

        var execution = await ProviderAuthFlow.ExecuteAsync(
            auth,
            allowRefresh: true,
            TestData.FixedNow,
            new SemaphoreSlim(1, 1),
            () => auth,
            (_, _) => false,
            value => value.RefreshToken,
            (value, _, _) =>
            {
                refreshCount++;
                return Task.FromResult(value with { Value = "unexpected" });
            },
            (value, _) => Task.FromResult(value.Value),
            CancellationToken.None);

        Assert.Equal(0, refreshCount);
        Assert.Equal("initial", execution.Result);
    }

    [Fact]
    public async Task ExecuteAsync_bypasses_refresh_lifecycle_when_disabled()
    {
        var readCount = 0;
        var refreshCount = 0;
        var auth = new Auth("initial", "refresh-token");

        var execution = await ProviderAuthFlow.ExecuteAsync(
            auth,
            allowRefresh: false,
            TestData.FixedNow,
            new SemaphoreSlim(1, 1),
            () =>
            {
                readCount++;
                return auth;
            },
            (_, _) => true,
            value => value.RefreshToken,
            (value, _, _) =>
            {
                refreshCount++;
                return Task.FromResult(value with { Value = "unexpected" });
            },
            (value, _) => Task.FromResult(value.Value),
            CancellationToken.None);

        Assert.Equal(0, readCount);
        Assert.Equal(0, refreshCount);
        Assert.Equal("initial", execution.Result);
    }

    [Fact]
    public async Task ExecuteAsync_refreshes_and_retries_once_on_auth_failure()
    {
        var attemptCount = 0;
        var refreshCount = 0;
        var auth = new Auth("old-token", "refresh-token");

        var execution = await ProviderAuthFlow.ExecuteAsync(
            auth,
            allowRefresh: true,
            TestData.FixedNow,
            new SemaphoreSlim(1, 1),
            () => auth,
            (_, _) => false,
            value => value.RefreshToken,
            (value, _, _) =>
            {
                refreshCount++;
                return Task.FromResult(value with { Value = "new-token" });
            },
            (value, _) =>
            {
                attemptCount++;
                return value.Value == "old-token"
                    ? throw new HttpRequestException("Unauthorized", inner: null, HttpStatusCode.Unauthorized)
                    : Task.FromResult($"success-with-{value.Value}");
            },
            CancellationToken.None);

        Assert.Equal(1, refreshCount);
        Assert.Equal(2, attemptCount);
        Assert.Equal("new-token", execution.Auth.Value);
        Assert.Equal("success-with-new-token", execution.Result);
    }

    [Fact]
    public async Task ExecuteAsync_throws_auth_failure_without_refresh_token()
    {
        var auth = new Auth("token", RefreshToken: null);

        await Assert.ThrowsAsync<HttpRequestException>(() => ProviderAuthFlow.ExecuteAsync<Auth, string>(
            auth,
            allowRefresh: true,
            TestData.FixedNow,
            new SemaphoreSlim(1, 1),
            () => auth,
            (_, _) => false,
            value => value.RefreshToken,
            (value, _, _) => Task.FromResult(value),
            (_, _) => throw new HttpRequestException("Unauthorized", inner: null, HttpStatusCode.Unauthorized),
            CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_uses_latest_persisted_auth_after_waiting_for_gate()
    {
        var stale = new Auth("stale", "refresh-token");
        var latest = new Auth("latest", "refresh-token");

        var execution = await ProviderAuthFlow.ExecuteAsync(
            stale,
            allowRefresh: true,
            TestData.FixedNow,
            new SemaphoreSlim(1, 1),
            () => latest,
            (_, _) => false,
            value => value.RefreshToken,
            (value, _, _) => Task.FromResult(value),
            (value, _) => Task.FromResult(value.Value),
            CancellationToken.None);

        Assert.Equal("latest", execution.Auth.Value);
        Assert.Equal("latest", execution.Result);
    }

    private sealed record Auth(string Value, string? RefreshToken);
}
