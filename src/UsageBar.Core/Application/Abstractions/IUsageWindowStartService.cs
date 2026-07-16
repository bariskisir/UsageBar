using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;

namespace UsageBar.Core.Application;

internal interface IUsageWindowStartService
{
    Task ObserveAsync(
        IReadOnlyList<UsageWindow> windows,
        AppSettings settings,
        CancellationToken cancellationToken);
}

internal interface IWindowStartRequestSender
{
    Task StartAsync(string providerName, string smallModelSelector, CancellationToken cancellationToken);
}
