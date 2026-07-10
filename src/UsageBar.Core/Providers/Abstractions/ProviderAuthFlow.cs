using System.Net;

namespace UsageBar.Core.Providers;

/// <summary>
/// Shared auth lifecycle helper for providers that can refresh credentials before a request
/// and retry exactly once after an authentication failure.
/// </summary>
internal static class ProviderAuthFlow
{
    public static async Task<AuthFlowResult<TAuth, TResult>> ExecuteAsync<TAuth, TResult>(
        TAuth auth,
        bool allowRefresh,
        DateTimeOffset now,
        SemaphoreSlim refreshGate,
        Func<TAuth?> readLatestAuth,
        Func<TAuth, DateTimeOffset, bool> shouldRefresh,
        Func<TAuth, string?> getRefreshToken,
        Func<TAuth, DateTimeOffset, CancellationToken, Task<TAuth>> refreshAsync,
        Func<TAuth, CancellationToken, Task<TResult>> executeAsync,
        CancellationToken cancellationToken)
        where TAuth : class
    {
        if (!allowRefresh)
        {
            var result = await executeAsync(auth, cancellationToken).ConfigureAwait(false);
            return new AuthFlowResult<TAuth, TResult>(auth, result);
        }

        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another caller may have refreshed and persisted the token while this caller
            // was waiting for the gate. Prefer that credential over the stale snapshot.
            auth = readLatestAuth() ?? auth;

            if (shouldRefresh(auth, now))
            {
                auth = await refreshAsync(auth, now, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var result = await executeAsync(auth, cancellationToken).ConfigureAwait(false);
                return new AuthFlowResult<TAuth, TResult>(auth, result);
            }
            catch (HttpRequestException exception) when (
                IsAuthenticationFailure(exception.StatusCode) && HasRefreshToken(auth, getRefreshToken))
            {
                auth = await refreshAsync(auth, now, cancellationToken).ConfigureAwait(false);
                var result = await executeAsync(auth, cancellationToken).ConfigureAwait(false);
                return new AuthFlowResult<TAuth, TResult>(auth, result);
            }
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private static bool HasRefreshToken<TAuth>(TAuth auth, Func<TAuth, string?> getRefreshToken) =>
        !string.IsNullOrWhiteSpace(getRefreshToken(auth));

    private static bool IsAuthenticationFailure(HttpStatusCode? statusCode) =>
        statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}

internal readonly record struct AuthFlowResult<TAuth, TResult>(TAuth Auth, TResult Result);
