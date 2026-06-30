using System.Net;

namespace UsageBar.Providers;

/// <summary>
/// Shared auth lifecycle helper for providers that can refresh credentials before a request
/// and retry exactly once after an authentication failure.
/// </summary>
internal static class ProviderAuthFlow
{
    public static async Task<TAuth> RefreshIfNeededAsync<TAuth>(
        TAuth auth,
        DateTimeOffset now,
        Func<TAuth, DateTimeOffset, bool> shouldRefresh,
        Func<TAuth, CancellationToken, Task<TAuth>> refreshAsync,
        CancellationToken cancellationToken)
    {
        return shouldRefresh(auth, now)
            ? await refreshAsync(auth, cancellationToken).ConfigureAwait(false)
            : auth;
    }

    public static async Task<TResult> ExecuteWithRefreshRetryAsync<TAuth, TResult>(
        TAuth auth,
        Func<TAuth, CancellationToken, Task<TResult>> executeAsync,
        Func<HttpStatusCode?, bool> isAuthFailure,
        Func<TAuth, bool> canRefresh,
        Func<TAuth, CancellationToken, Task<TAuth>> refreshAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            return await executeAsync(auth, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (isAuthFailure(exception.StatusCode) && canRefresh(auth))
        {
            var refreshed = await refreshAsync(auth, cancellationToken).ConfigureAwait(false);
            return await executeAsync(refreshed, cancellationToken).ConfigureAwait(false);
        }
    }
}
