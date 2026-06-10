using UsageBar.Domain;

namespace UsageBar.Application;

public interface IRemoteNotificationService
{
    Task SendAsync(NotificationLevel level, string message, CancellationToken cancellationToken);
}
