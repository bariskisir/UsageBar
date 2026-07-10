using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;

namespace UsageBar.Core.Application;

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
                // Only emit a reset notification when the window was previously in a
                // warning state (high, critical, or limit). A minor fluctuation
                // below an already-low level is noise, not a meaningful reset.
                if (_firedLevel.Remove(key))
                {
                    notifications.Add(new ThresholdNotification(
                        NotificationLevel.Reset,
                        $"{current.ProviderName} {current.Label} reset to {DisplayPercent(currentFraction)}%"));
                }

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
        PurgeStaleState(currentWindows);
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

    private void PurgeStaleState(IReadOnlyList<UsageWindow> currentWindows)
    {
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var window in currentWindows)
        {
            active.Add($"{window.ProviderName}|{window.Label}");
        }

        var stale = new List<string>();
        foreach (var key in _firedLevel.Keys)
        {
            if (!active.Contains(key))
            {
                stale.Add(key);
            }
        }

        foreach (var key in stale)
        {
            _firedLevel.Remove(key);
        }
    }
}
