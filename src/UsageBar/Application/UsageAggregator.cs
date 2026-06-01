using UsageBar.Domain;
using UsageBar.Infrastructure.Diagnostics;

namespace UsageBar.Application;

internal static class UsageAggregator
{
    public static async Task<UsageSnapshot> RefreshAsync(
        IReadOnlyList<IUsageProvider> providers,
        ProviderCredentials credentials,
        AppLogger logger)
    {
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var providerNames = new List<string>(providers.Count);
        var refreshTasks = providers
            .Select(provider =>
            {
                providerNames.Add(provider.Name);
                return RefreshProviderAsync(provider, credentials, logger, cancellationSource.Token);
            })
            .ToArray();

        var results = await Task.WhenAll(refreshTasks).ConfigureAwait(false);
        var blocks = new List<UsageBlock>();
        var windows = new List<UsageBarWindow>();
        var plans = new List<(string Provider, string Plan)>();

        for (var i = 0; i < results.Length; i++)
        {
            var result = results[i];
            if (result is null)
            {
                continue;
            }

            if (result.Plan is not null)
            {
                plans.Add((providerNames[i], result.Plan));
            }

            blocks.AddRange(result.Blocks);
            if (result.Windows is not null)
            {
                windows.AddRange(result.Windows);
            }
        }

        return new UsageSnapshot(blocks, windows, plans);
    }

    private static async Task<ProviderResult?> RefreshProviderAsync(
        IUsageProvider provider,
        ProviderCredentials credentials,
        AppLogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.GetUsageAsync(credentials, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await logger.LogAsync($"{provider.Name} refresh failed.", exception).ConfigureAwait(false);
            return null;
        }
    }
}

internal sealed record UsageSnapshot(
    IReadOnlyList<UsageBlock> Blocks,
    IReadOnlyList<UsageBarWindow> Windows,
    IReadOnlyList<(string Provider, string Plan)> Plans);
