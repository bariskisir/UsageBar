namespace UsageBar.Core.Application;

public interface IRefreshCycleRunner
{
    Task<RefreshOutcome> RunAsync(string trigger, CancellationToken cancellationToken);

    Task SendTestNotificationAsync(CancellationToken cancellationToken);
}

public readonly record struct RefreshOutcome(int RefreshMinutes);