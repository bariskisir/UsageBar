using System.Diagnostics;
using Microsoft.Extensions.Logging;
using UsageBar.Core.Domain;
using UsageBar.Core.Providers;

namespace UsageBar.Core.Application;

/// <summary>Queries configured providers concurrently and isolates provider-local failures.</summary>
internal static class UsageAggregator
{
    private static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(45);

    public static async Task<UsageSnapshot> RefreshAsync(
        IReadOnlyList<IUsageProvider> providers,
        ProviderQueryContext context,
        ILogger logger,
        CancellationToken cancellationToken,
        IReadOnlyList<ProviderSettings>? providerSettings = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RefreshTimeout);

        var ordered = providers.OrderBy(provider => provider.Descriptor.DisplayOrder).ToArray();
        var settingsByName = (providerSettings ?? [])
            .GroupBy(settings => settings.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var configured = new List<IUsageProvider>(ordered.Length);

        foreach (var provider in ordered)
        {
            if (settingsByName.TryGetValue(provider.Descriptor.Name, out var settings) && !settings.Enabled)
            {
                logger.LogDebug("{Provider} skipped because it is disabled in settings.", provider.Descriptor.Name);
                continue;
            }

            try
            {
                if (provider.IsConfigured(context))
                {
                    configured.Add(provider);
                }
                else
                {
                    logger.LogDebug("{Provider} skipped because no usable credential was found.", provider.Descriptor.Name);
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "{Provider} credential check failed; provider skipped for this refresh.", provider.Descriptor.Name);
            }
        }

        logger.LogInformation(
            "Provider selection completed: registered={RegisteredCount}; configured={ConfiguredCount}.",
            ordered.Length,
            configured.Count);

        var refreshes = await Task.WhenAll(configured.Select(provider =>
                RefreshProviderAsync(provider, context, logger, timeout.Token, cancellationToken)))
            .ConfigureAwait(false);

        var succeeded = new List<(ProviderResult Result, int DisplayOrder)>();
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

        return new UsageSnapshot(orderedResults, windows);
    }

    private static async Task<ProviderRefresh> RefreshProviderAsync(
        IUsageProvider provider,
        ProviderQueryContext context,
        ILogger logger,
        CancellationToken refreshCancellationToken,
        CancellationToken shutdownCancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["Provider"] = provider.Descriptor.Name,
        });

        logger.LogInformation("Provider query started.");

        try
        {
            var results = await provider.QueryAsync(context, refreshCancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Provider query completed: resultCount={ResultCount}; windowCount={WindowCount}; durationMs={DurationMs:F1}.",
                results.Count,
                results.OfType<MetricResult>().Sum(result => result.Windows.Count),
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return new ProviderRefresh(provider, results);
        }
        catch (OperationCanceledException) when (shutdownCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Provider query timed out after {DurationMs:F1} ms.",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return new ProviderRefresh(provider, []);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Provider query failed after {DurationMs:F1} ms.",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return new ProviderRefresh(provider, []);
        }
    }

    private readonly record struct ProviderRefresh(IUsageProvider Provider, IReadOnlyList<ProviderResult> Results);
}
