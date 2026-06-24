namespace UsageBar.Application;

public interface IUsageRefreshService
{
    void Start();
    void Stop();
    void TriggerManualRefresh();
    void SendTestNotification();
}
