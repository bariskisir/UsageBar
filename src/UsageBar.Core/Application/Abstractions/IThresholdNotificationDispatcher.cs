using UsageBar.Configuration;
using UsageBar.Domain;

namespace UsageBar.Application;

internal interface IThresholdNotificationDispatcher
{
    void SendTestNotification();

    Task EmitAsync(IReadOnlyList<UsageWindow> windows, AppSettings settings, CancellationToken cancellationToken = default);
}
