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
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RefreshTimeout);

        // Query in display order so the resulting tray bars and tooltip cards come out ordered.
        var ordered = providers.OrderBy(provider => provider.Descriptor.DisplayOrder).ToArray();
        var tasks = ordered
            .Select(provider => RefreshProviderAsync(provider, context, logger, timeout.Token))
            .ToArray();

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var succeeded = new List<ProviderResult>(results.Length);
        var windows = new List<UsageWindow>();

        foreach (var result in results)
        {
            if (result is null)
            {
                continue;
            }

            succeeded.Add(result);

            if (result is MetricResult metric)
            {
                windows.AddRange(metric.Windows);
            }
        }

        return new UsageSnapshot(succeeded, windows);
    }

    private static async Task<ProviderResult?> RefreshProviderAsync(
        IUsageProvider provider,
        ProviderQueryContext context,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.GetUsageAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "{Provider} refresh failed.", provider.Descriptor.Name);
            return null;
        }
    }
}
