using UsageBar.Core.Configuration;

namespace UsageBar.Core.Application;

public interface IRemoteNotificationService
{
    Task SendAsync(string message, AppSettings settings, CancellationToken cancellationToken);
}