using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;

namespace UsageBar.Core.Application;

internal interface IThresholdNotificationDispatcher
{
    Task SendTestNotificationAsync(AppSettings settings, CancellationToken cancellationToken = default);

    Task EmitAsync(IReadOnlyList<UsageWindow> windows, AppSettings settings, CancellationToken cancellationToken = default);
}