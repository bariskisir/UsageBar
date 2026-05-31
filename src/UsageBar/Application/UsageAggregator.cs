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
        var refreshTasks = providers
            .Select(provider => RefreshProviderAsync(provider, credentials, logger, cancellationSource.Token))
            .ToArray();

        var results = await Task.WhenAll(refreshTasks).ConfigureAwait(false);
        var blocks = new List<UsageBlock>();
        double? codexPrimaryUsedPercent = null;
        double? codexSecondaryUsedPercent = null;

        foreach (var result in results)
        {
            if (result is null)
            {
                continue;
            }

            blocks.AddRange(result.Blocks);
            codexPrimaryUsedPercent ??= result.CodexPrimaryUsedPercent;
            codexSecondaryUsedPercent ??= result.CodexSecondaryUsedPercent;
        }

        return new UsageSnapshot(blocks, codexPrimaryUsedPercent, codexSecondaryUsedPercent);
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
    double? CodexPrimaryUsedPercent,
    double? CodexSecondaryUsedPercent);
