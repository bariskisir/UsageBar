namespace UsageBar.Core.Application;

public interface IUsageRefreshService
{
    Task RunAsync(CancellationToken cancellationToken);

    void RequestManualRefresh();

    Task SendTestNotificationAsync(CancellationToken cancellationToken = default);
}
