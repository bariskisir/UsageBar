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
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Lock _timerGate = new();
    private Timer? _timer;
    private bool _stopped;

    public RefreshCoordinator(
        SettingsService settings,
        AppLogger logger,
        IReadOnlyList<IUsageProvider> providers,
        TrayIcon trayIcon)
    {
        _settings = settings;
        _logger = logger;
        _providers = providers;
        _trayIcon = trayIcon;
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
            _trayIcon.UpdateIcon(snapshot.CodexPrimaryUsedPercent);
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
