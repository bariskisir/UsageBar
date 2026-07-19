using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;

namespace UsageBar.Core.Application;

/// <summary>
/// Compares per-window usage between refreshes and emits notifications for threshold crossings
/// and usage resets.
/// </summary>
internal sealed class ThresholdNotifier
{
    private IReadOnlyList<UsageWindow> _previousWindows = [];

    public IReadOnlyList<ThresholdNotification> Evaluate(IReadOnlyList<UsageWindow> currentWindows, AppSettings settings)
    {
        var notification = settings.Notification ?? NotificationSettings.Default;
        var high = notification.High / 100.0;
        var critical = notification.Critical / 100.0;
        var notifications = new List<ThresholdNotification>();

        foreach (var current in currentWindows)
        {
            // When a window appears for the first time (or reappears after a gap), skip
            // notification evaluation — there is no real baseline to compare against.
            // The window is recorded as-is in _previousWindows below, so the next refresh
            // has a genuine previous value for threshold comparison. Without this, a fresh
            // launch with e.g. 80% usage would fire a spurious high notification because
            // the synthetic 0% baseline makes it look like a sudden jump.
            var previous = FindWindow(_previousWindows, current.ProviderName, current.Label, current.SubLabel);
            if (previous is null)
            {
                continue;
            }

            var currentFraction = current.UsedPercent / 100.0;
            var previousFraction = previous.UsedPercent / 100.0;

            var windowLabel = string.IsNullOrEmpty(current.SubLabel)
                ? current.Label
                : $"{current.Label} ({current.SubLabel})";

            if (currentFraction < previousFraction)
            {
                // Only emit a reset notification when usage drops to a genuinely low
                // level (<= 5%), or when the provider confirms a new window by advancing
                // its reset timestamp. Partial decreases with the same window deadline
                // are not real resets and should not be announced.
                var resetTimestampAdvanced = current.ResetAt is { } currentResetAt
                    && previous.ResetAt is { } previousResetAt
                    && currentResetAt > previousResetAt;
                if (currentFraction <= 0.05 || resetTimestampAdvanced)
                {
                    notifications.Add(new ThresholdNotification(
                        NotificationLevel.Reset,
                        $"{current.ProviderName} {windowLabel} reset to {DisplayPercent(currentFraction)}%"));
                }

                continue;
            }

            if (previousFraction < 1.0 && currentFraction >= 1.0)
            {
                notifications.Add(new ThresholdNotification(
                    NotificationLevel.Critical,
                    $"{current.ProviderName} {windowLabel} at 100% — limit reached!"));
            }
            else if (previousFraction < critical && currentFraction >= critical)
            {
                notifications.Add(new ThresholdNotification(
                    NotificationLevel.Critical,
                    $"{current.ProviderName} {windowLabel} at {DisplayPercent(currentFraction)}% — critically high!"));
            }
            else if (previousFraction < high && currentFraction >= high)
            {
                notifications.Add(new ThresholdNotification(
                    NotificationLevel.High,
                    $"{current.ProviderName} {windowLabel} at {DisplayPercent(currentFraction)}% — approaching limit"));
            }
        }

        _previousWindows = currentWindows;
        return notifications;
    }

    private static int DisplayPercent(double fraction) => (int)Math.Round(fraction * 100);

    private static UsageWindow? FindWindow(IReadOnlyList<UsageWindow> windows, string provider, string label, string? subLabel)
    {
        foreach (var window in windows)
        {
            if (window.ProviderName == provider && window.Label == label && window.SubLabel == subLabel)
            {
                return window;
            }
        }

        return null;
    }
}
