namespace UsageBar.Application;

public interface IRemoteNotificationService
{
    Task SendAsync(string message, CancellationToken cancellationToken);
}
