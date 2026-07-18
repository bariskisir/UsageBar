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

internal sealed record WindowStartRequest(
    string ProviderName,
    string SmallModelSelector,
    string? WindowLabel = null,
    string? WindowSubLabel = null);

internal interface IWindowStartRequestSender
{
    Task StartAsync(WindowStartRequest request, CancellationToken cancellationToken);
}
