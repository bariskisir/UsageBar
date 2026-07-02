using Microsoft.Extensions.Logging;
using UsageBar.Domain;
using UsageBar.Providers;

namespace UsageBar.Application;

/// <summary>
/// Queries all providers concurrently (in display order) and merges their results into a single
/// <see cref="UsageSnapshot"/>. Provider failures are logged and isolated so one bad provider
/// never breaks the refresh.
/// </summary>
internal static class UsageAggregator
{
    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(45);

    public static async Task<UsageSnapshot> RefreshAsync(
        IReadOnlyList<IUsageProvider> providers,
        ProviderQueryContext context,
        ILogger logger,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? hiddenProviders = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RefreshTimeout);

        // Query in display order so the resulting tray bars and tooltip cards come out ordered.
        var ordered = providers.OrderBy(provider => provider.Descriptor.DisplayOrder).ToArray();

        // Check credentials for every provider before each refresh so providers that
        // gain credentials mid-session are automatically picked up, and providers
        // without credentials are skipped without creating tasks for them.
        foreach (var provider in ordered)
        {
            try
            {
                provider.RefreshEnabled(context);
            }
            catch (Exception exception)
            {
                // A failing credential check (e.g. corrupted auth file, locked registry)
                // should not prevent other providers from being queried. Log and treat
                // the provider as disabled for this cycle.
                logger.LogWarning(exception, "{Provider} credential check failed — provider disabled for this cycle.", provider.Descriptor.Name);
                provider.Descriptor.IsEnabled = false;
            }
        }

        // Apply user-hidden providers — disable them from this refresh cycle.
        if (hiddenProviders is { Count: > 0 })
        {
            foreach (var provider in ordered)
            {
                if (provider.Descriptor.IsEnabled && hiddenProviders.Contains(provider.Descriptor.Name))
                {
                    provider.Descriptor.IsEnabled = false;
                }
            }
        }

        var tasks = ordered
            .Where(provider => provider.Descriptor.IsEnabled)
            .Select(provider => RefreshProviderAsync(provider, context, logger, timeout.Token))
            .ToArray();

        var refreshes = await Task.WhenAll(tasks).ConfigureAwait(false);

        var succeeded = new List<(ProviderResult Result, int DisplayOrder)>(refreshes.Length);

        foreach (var refresh in refreshes)
        {
            foreach (var result in refresh.Results)
            {
                var displayOrder = refresh.Provider is IResultDisplayOrderProvider dynamicOrder
                    ? dynamicOrder.GetDisplayOrder(result)
                    : refresh.Provider.Descriptor.DisplayOrder;

                succeeded.Add((result, displayOrder));
            }
        }

        var orderedResults = succeeded
            .OrderBy(item => item.DisplayOrder)
            .Select(item => item.Result)
            .ToList();
        var windows = orderedResults
            .OfType<MetricResult>()
            .SelectMany(metric => metric.Windows)
            .ToList();

        return new UsageSnapshot(
            orderedResults,
            windows);
    }

    private static async Task<ProviderRefresh> RefreshProviderAsync(
        IUsageProvider provider,
        ProviderQueryContext context,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = await provider.GetUsageResultsAsync(context, cancellationToken).ConfigureAwait(false);
            return new ProviderRefresh(provider, results);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a provider failure — propagate so the refresh can abort
            // promptly on shutdown instead of logging a warning per provider.
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "{Provider} refresh failed.", provider.Descriptor.Name);
            return new ProviderRefresh(provider, Results: Array.Empty<ProviderResult>());
        }
    }

    private readonly record struct ProviderRefresh(IUsageProvider Provider, IReadOnlyList<ProviderResult> Results);
}
