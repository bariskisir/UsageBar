using UsageBar.Configuration;
using UsageBar.Domain;

namespace UsageBar.Application;

internal sealed class ThresholdNotificationDispatcher : IThresholdNotificationDispatcher
{
    private static readonly NotificationLevel[] SeverityOrder =
        [NotificationLevel.Critical, NotificationLevel.High, NotificationLevel.Reset];

    private readonly IUsageView _view;
    private readonly IReadOnlyList<IRemoteNotificationService> _remoteServices;
    private readonly ThresholdNotifier _thresholds = new();

    public ThresholdNotificationDispatcher(
        IUsageView view,
        IEnumerable<IRemoteNotificationService> remoteServices)
    {
        _view = view;
        _remoteServices = remoteServices.ToArray();
    }

    public void SendTestNotification()
    {
        var message = NotificationMessageFormatter.Format(NotificationLevel.Critical, "Test: Limit reached 100%");
        _view.Notify(NotificationLevel.Critical, message);
        foreach (var service in _remoteServices)
        {
            _ = service.SendAsync(message, CancellationToken.None);
        }
    }

    public async Task EmitAsync(IReadOnlyList<UsageWindow> windows, AppSettings settings)
    {
        var notifications = _thresholds.Evaluate(windows, settings);
        if (notifications.Count == 0)
        {
            return;
        }

        foreach (var level in SeverityOrder)
        {
            var lines = notifications
                .Where(notification => notification.Level == level)
                .Select(notification => notification.Message)
                .ToList();
            if (lines.Count == 0)
            {
                continue;
            }

            var message = NotificationMessageFormatter.Format(level, string.Join(Environment.NewLine, lines));
            _view.Notify(level, message);

            foreach (var service in _remoteServices)
            {
                await service
                    .SendAsync(message, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
    }
}
