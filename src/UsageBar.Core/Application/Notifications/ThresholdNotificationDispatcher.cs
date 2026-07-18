using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;

namespace UsageBar.Core.Application;

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

    public async Task SendTestNotificationAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var message = NotificationMessageFormatter.Format(NotificationLevel.Critical, "Test: Limit reached 100%");
        _view.Notify(NotificationLevel.Critical, message);
        await Task.WhenAll(_remoteServices.Select(service => service.SendAsync(message, settings, cancellationToken)))
            .ConfigureAwait(false);
    }

    public async Task EmitAsync(IReadOnlyList<UsageWindow> windows, AppSettings settings, CancellationToken cancellationToken = default)
    {
        if (settings.Notification is { Enabled: false })
        {
            return;
        }

        var notifications = _thresholds.Evaluate(windows, settings);
        if (notifications.Count == 0)
        {
            return;
        }

        foreach (var level in SeverityOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();

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

            if (level != NotificationLevel.Reset)
            {
                await Task.WhenAll(_remoteServices.Select(service => service.SendAsync(message, settings, cancellationToken)))
                    .ConfigureAwait(false);
            }
        }
    }
}