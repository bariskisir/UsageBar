using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>
/// A source of usage or balance information. Implement this directly for metric
/// providers (Codex, Claude, ElevenLabs); derive from <see cref="BalanceUsageProvider"/> for
/// balance providers (DeepSeek, OpenRouter, Moonshot, Deepgram).
/// </summary>
public interface IUsageProvider
{
    /// <summary>Static identity and presentation metadata (name, category, display order).</summary>
    ProviderDescriptor Descriptor { get; }

    /// <summary>
    /// Queries the provider for the current refresh.
    /// </summary>
    /// <returns>
    /// A <see cref="ProviderResult"/>, or <see langword="null"/> when the provider is
    /// not configured (missing credentials/auth). Throws <see cref="ProviderException"/>
    /// (or other exceptions) on API or parsing failures, which the aggregator logs and isolates.
    /// </returns>
    Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Queries the provider and returns zero or more results. The default implementation
    /// wraps <see cref="GetUsageAsync"/> to return a single result (or empty when null).
    /// Override to return multiple results (e.g. a metric and a balance card together).
    /// </summary>
    async Task<IReadOnlyList<ProviderResult>> GetUsageResultsAsync(ProviderQueryContext context, CancellationToken cancellationToken)
    {
        var result = await GetUsageAsync(context, cancellationToken).ConfigureAwait(false);
        return result is null ? Array.Empty<ProviderResult>() : new ProviderResult[] { result };
    }

    /// <summary>
    /// Called by the aggregator before each refresh to let the provider check whether
    /// credentials exist (API key, auth file, etc.) and update
    /// <see cref="ProviderDescriptor.IsEnabled"/> accordingly. The default enables the
    /// provider (test/mock providers have no credentials to check). Real providers
    /// override this to set <c>IsEnabled = false</c> when their API key or auth file
    /// is missing.
    /// </summary>
    void RefreshEnabled(ProviderQueryContext context) => Descriptor.IsEnabled = true;
}
