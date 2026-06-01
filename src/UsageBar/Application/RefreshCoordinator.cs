using UsageBar.Domain;
using UsageBar.Infrastructure.Configuration;
using UsageBar.Infrastructure.Diagnostics;
using UsageBar.Shell.Tray;

namespace UsageBar.Application;

internal sealed class RefreshCoordinator : IDisposable
{
    private readonly SettingsService _settings;
    private readonly AppLogger _logger;
    private readonly IReadOnlyList<IUsageProvider> _providers;
    private readonly TrayIcon _trayIcon;
    private readonly WebViewTooltip? _tooltip;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Lock _timerGate = new();
    private Timer? _timer;
    private IReadOnlyList<UsageBarWindow> _previousWindows = [];
    private readonly Dictionary<string, byte> _notifiedLevel = new(); // "Provider|Label" → 0/1/2
    private bool _stopped;

    public RefreshCoordinator(
        SettingsService settings,
        AppLogger logger,
        IReadOnlyList<IUsageProvider> providers,
        TrayIcon trayIcon,
        WebViewTooltip? tooltip = null)
    {
        _settings = settings;
        _logger = logger;
        _providers = providers;
        _trayIcon = trayIcon;
        _tooltip = tooltip;
    }

    public void Start()
    {
        _trayIcon.UpdateTooltip("UsageBar\nLoading...");
        _ = RefreshAsync(null);
    }

    public void TriggerManualRefresh()
    {
        var anchor = DateTimeOffset.Now;
        DisableTimer();
        _ = RefreshAsync(anchor);
    }

    public void Stop()
    {
        lock (_timerGate)
        {
            _stopped = true;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private async Task RefreshAsync(DateTimeOffset? scheduleAnchor)
    {
        if (!await _refreshGate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        AppSettings settings;

        try
        {
            settings = await _settings.ReadAsync().ConfigureAwait(false);
            var snapshot = await UsageAggregator.RefreshAsync(
                _providers,
                settings.ToProviderCredentials(),
                _logger).ConfigureAwait(false);

            _trayIcon.UpdateTooltip(TooltipFormatter.Format(snapshot.Blocks));
            _trayIcon.UpdateIcon(snapshot.Windows, snapshot.Plans);

            // Push tooltip cards to WebView2 popup.
            if (_tooltip is not null)
            {
                var cards = TooltipCardBuilder.Build(snapshot);
                _trayIcon.UpdateCards(cards);
            }

            CheckThresholdCrossings(snapshot.Windows, settings);
        }
        catch (Exception exception)
        {
            settings = AppSettings.Default;
            await _logger.LogAsync("Unexpected refresh failure.", exception).ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
        }

        ScheduleNext(settings.RefreshPeriodMinute, scheduleAnchor);
    }

    private void CheckThresholdCrossings(
        IReadOnlyList<UsageBarWindow> currentWindows,
        AppSettings settings)
    {
        var high = settings.HighPercentage / 100.0;
        var critical = settings.CriticalPercentage / 100.0;
        var messages = new List<string>(capacity: 4);

        foreach (var current in currentWindows)
        {
            var previous = FindWindow(_previousWindows, current.ProviderName, current.WindowLabel);
            if (previous is null)
            {
                continue;
            }

            var key = $"{current.ProviderName}|{current.WindowLabel}";
            if (!_notifiedLevel.TryGetValue(key, out var level))
            {
                level = 0;
            }

            var currPct = current.UsedPercent / 100.0;
            var prevPct = previous.UsedPercent / 100.0;

            // Reset: usage dropped.
            if (currPct < prevPct)
            {
                var pctDisplay = (int)Math.Round(currPct * 100);
                messages.Add($"{current.ProviderName} {current.WindowLabel} reset to {pctDisplay}%");
                _notifiedLevel.Remove(key);
                continue;
            }

            // Climb to critical.
            if (level < 2 && prevPct < critical && currPct >= critical)
            {
                var pctDisplay = (int)Math.Round(currPct * 100);
                messages.Add($"{current.ProviderName} {current.WindowLabel} at {pctDisplay}% — critically high!");
                _notifiedLevel[key] = 2;
            }
            // Climb to high.
            else if (level < 1 && prevPct < high && currPct >= high)
            {
                var pctDisplay = (int)Math.Round(currPct * 100);
                messages.Add($"{current.ProviderName} {current.WindowLabel} at {pctDisplay}% — approaching limit");
                _notifiedLevel[key] = 1;
            }
        }

        _previousWindows = currentWindows
            .Select(w => new UsageBarWindow(w.ProviderName, w.WindowLabel, w.UsedPercent))
            .ToList();

        if (messages.Count > 0)
        {
            _trayIcon.ShowNotification(string.Join(Environment.NewLine, messages));
        }
    }

    private static UsageBarWindow? FindWindow(IReadOnlyList<UsageBarWindow> windows, string provider, string label)
    {
        foreach (var w in windows)
        {
            if (w.ProviderName == provider && w.WindowLabel == label)
            {
                return w;
            }
        }

        return null;
    }

    private void ScheduleNext(int refreshPeriodMinute, DateTimeOffset? scheduleAnchor)
    {
        lock (_timerGate)
        {
            if (_stopped)
            {
                return;
            }

            var period = TimeSpan.FromMinutes(refreshPeriodMinute);
            var dueTime = period;

            if (scheduleAnchor is not null)
            {
                var elapsed = DateTimeOffset.Now - scheduleAnchor.Value;
                dueTime = elapsed >= period ? TimeSpan.Zero : period - elapsed;
            }

            _timer?.Dispose();
            _timer = new Timer(_ => _ = RefreshAsync(null), null, dueTime, Timeout.InfiniteTimeSpan);
        }
    }

    private void DisableTimer()
    {
        lock (_timerGate)
        {
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        Stop();
        _refreshGate.Dispose();
    }
}
