using UsageBar.Configuration;
using UsageBar.Domain;

namespace UsageBar.Application;

/// <summary>
/// Tracks per-window usage between refreshes and emits one-shot high/critical/limit-reached
/// notifications plus reset notifications. Each window fires at most once per milestone per
/// episode; a usage drop resets that window's state.
/// </summary>
internal sealed class ThresholdNotifier
{
    private const byte NoneFired = 0;
    private const byte HighFired = 1;
    private const byte CriticalFired = 2;
    private const byte LimitFired = 3;

    private readonly Dictionary<string, byte> _firedLevel = new(StringComparer.Ordinal);
    private IReadOnlyList<UsageWindow> _previousWindows = [];

    public IReadOnlyList<ThresholdNotification> Evaluate(IReadOnlyList<UsageWindow> currentWindows, AppSettings settings)
    {
        var high = settings.HighPercentage / 100.0;
        var critical = settings.CriticalPercentage / 100.0;
        var notifications = new List<ThresholdNotification>();

        foreach (var current in currentWindows)
        {
            var previous = FindWindow(_previousWindows, current.ProviderName, current.Label);
            if (previous is null)
            {
                continue;
            }

            var key = $"{current.ProviderName}|{current.Label}";
            var fired = _firedLevel.GetValueOrDefault(key, NoneFired);

            var currentFraction = current.UsedPercent / 100.0;
            var previousFraction = previous.UsedPercent / 100.0;

            if (currentFraction < previousFraction)
            {
                notifications.Add(new ThresholdNotification(
                    NotificationLevel.Reset,
                    $"{current.ProviderName} {current.Label} reset to {DisplayPercent(currentFraction)}%"));
                _firedLevel.Remove(key);
                continue;
            }

            if (fired < LimitFired && previousFraction < 1.0 && currentFraction >= 1.0)
            {
                notifications.Add(new ThresholdNotification(
                    NotificationLevel.Critical,
                    $"{current.ProviderName} {current.Label} at 100% — limit reached!"));
                _firedLevel[key] = LimitFired;
            }
            else if (fired < CriticalFired && previousFraction < critical && currentFraction >= critical)
            {
                notifications.Add(new ThresholdNotification(
                    NotificationLevel.Critical,
                    $"{current.ProviderName} {current.Label} at {DisplayPercent(currentFraction)}% — critically high!"));
                _firedLevel[key] = CriticalFired;
            }
            else if (fired < HighFired && previousFraction < high && currentFraction >= high)
            {
                notifications.Add(new ThresholdNotification(
                    NotificationLevel.High,
                    $"{current.ProviderName} {current.Label} at {DisplayPercent(currentFraction)}% — approaching limit"));
                _firedLevel[key] = HighFired;
            }
        }

        _previousWindows = currentWindows;
        return notifications;
    }

    private static int DisplayPercent(double fraction) => (int)Math.Round(fraction * 100);

    private static UsageWindow? FindWindow(IReadOnlyList<UsageWindow> windows, string provider, string label)
    {
        foreach (var window in windows)
        {
            if (window.ProviderName == provider && window.Label == label)
            {
                return window;
            }
        }

        return null;
    }
}
