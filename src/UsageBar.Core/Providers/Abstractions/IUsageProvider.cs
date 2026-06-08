using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>
/// A source of usage or balance information. Implement this directly for metric
/// providers (Codex, Claude); derive from <see cref="BalanceUsageProvider"/> for
/// balance providers (DeepSeek, OpenRouter, Deepgram).
/// </summary>
public interface IUsageProvider
{
    /// <summary>Display name, e.g. "Codex" or "DeepSeek".</summary>
    string Name { get; }

    /// <summary>Whether this provider reports usage windows or a balance.</summary>
    ProviderCategory Category { get; }

    /// <summary>
    /// Queries the provider for the current refresh.
    /// </summary>
    /// <returns>
    /// A <see cref="ProviderResult"/>, or <see langword="null"/> when the provider is
    /// not configured (missing credentials/auth). Throws <see cref="ProviderException"/>
    /// (or other exceptions) on API or parsing failures, which the aggregator logs and isolates.
    /// </returns>
    Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken);
}
