using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>
/// A source of usage or balance information. Implement this directly for metric
/// providers (Codex, Claude, ElevenLabs); derive from <see cref="BalanceUsageProvider"/> for
/// balance providers (DeepSeek, OpenRouter, Moonshot, Deepgram).
/// </summary>
public interface IUsageProvider
{
    /// <summary>Static identity and presentation metadata (name, category, display order).</summary>
    ProviderDescriptor Descriptor { get; }

    /// <summary>Returns whether this provider has usable credentials for this refresh.</summary>
    bool IsConfigured(ProviderQueryContext context) => true;

    /// <summary>Queries the provider and returns every result produced by the request.</summary>
    Task<IReadOnlyList<ProviderResult>> QueryAsync(
        ProviderQueryContext context,
        CancellationToken cancellationToken);
}

/// <summary>Adapter contract for providers that produce at most one result.</summary>
public interface ISingleResultUsageProvider : IUsageProvider
{
    Task<ProviderResult?> GetUsageAsync(ProviderQueryContext context, CancellationToken cancellationToken);

    async Task<IReadOnlyList<ProviderResult>> IUsageProvider.QueryAsync(
        ProviderQueryContext context,
        CancellationToken cancellationToken)
    {
        var result = await GetUsageAsync(context, cancellationToken).ConfigureAwait(false);
        return result is null ? [] : [result];
    }
}