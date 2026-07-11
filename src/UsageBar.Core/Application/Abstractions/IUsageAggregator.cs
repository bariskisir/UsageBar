using UsageBar.Core.Domain;
using UsageBar.Core.Providers;

namespace UsageBar.Core.Application;

public interface IUsageAggregator
{
    Task<UsageSnapshot> RefreshAsync(
        IReadOnlyList<IUsageProvider> providers,
        ProviderQueryContext context,
        CancellationToken cancellationToken,
        IReadOnlyList<ProviderSettings>? providerSettings = null);
}