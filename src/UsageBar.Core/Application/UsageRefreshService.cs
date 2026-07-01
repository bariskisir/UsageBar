using Microsoft.Extensions.Logging;
using UsageBar.Configuration;
using UsageBar.Providers;

namespace UsageBar.Application;

/// <summary>
/// Owns the refresh lifecycle: an initial refresh, periodic scheduled refreshes, and
/// on-demand manual refreshes. Each refresh reads settings, queries providers, updates the
/// view (icon + tooltip cards), and emits threshold notifications. Refreshes never overlap.
/// </summary>
public sealed class UsageRefreshService : IUsageRefreshService, IDisposable
{
    private readonly IReadOnlyList<IUsageProvider> _providers;
    private readonly ISettingsStore _settings;
    private readonly IUsageView _view;
    private readonly IClock _clock;
    private readonly IThresholdNotificationDispatcher _notifications;
    private readonly ILogger<UsageRefreshService> _logger;

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Lock _timerGate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Timer? _timer;
    private bool _stopped;

    public UsageRefreshService(
        IEnumerable<IUsageProvider> providers,
        ISettingsStore settings,
        IUsageView view,
        IClock clock,
        IEnumerable<IRemoteNotificationService> remoteServices,
        ILogger<UsageRefreshService> logger)
        : this(
            providers,
            settings,
            view,
            clock,
            new ThresholdNotificationDispatcher(view, remoteServices),
            logger)
    {
    }

    internal UsageRefreshService(
        IEnumerable<IUsageProvider> providers,
        ISettingsStore settings,
        IUsageView view,
        IClock clock,
        IThresholdNotificationDispatcher notifications,
        ILogger<UsageRefreshService> logger)
    {
        _providers = providers.ToArray();
        _settings = settings;
        _view = view;
        _clock = clock;
        _notifications = notifications;
        _logger = logger;
    }

    /// <summary>Starts the first refresh; the next one is scheduled when it completes.</summary>
    public void Start() => _ = RefreshAsync(scheduleAnchor: null);

    /// <summary>Refreshes immediately and reschedules the periodic timer from now.</summary>
    public void TriggerManualRefresh()
    {
        var anchor = _clock.Now;
        DisableTimer();
        _ = RefreshAsync(anchor);
    }

    public void SendTestNotification() => _notifications.SendTestNotification();

    /// <summary>Stops scheduling further refreshes and signals the in-flight refresh to abort.</summary>
    public void Stop()
    {
        _shutdown.Cancel();

        lock (_timerGate)
        {
            _stopped = true;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private async Task RefreshAsync(DateTimeOffset? scheduleAnchor)
    {
        // Single-flight: skip if a refresh is already running, but still reschedule so the
        // periodic cycle never silently dies when a refresh overruns its timer period.
        if (!await _refreshGate.WaitAsync(0).ConfigureAwait(false))
        {
            _ = ScheduleNextFallbackAsync(scheduleAnchor);
            return;
        }

        AppSettings settings = AppSettings.Default;

        try
        {
            settings = await _settings.ReadAsync(_shutdown.Token).ConfigureAwait(false);

            var context = ProviderQueryContext.FromSettings(settings, _clock.Now);
            var snapshot = await UsageAggregator
                .RefreshAsync(_providers, context, _logger, _shutdown.Token)
                .ConfigureAwait(false);

            // Force auto icon layout in test mode so all bar windows are visible regardless
            // of the user's saved iconLayout settings.
            var iconLayout = Environment.GetEnvironmentVariable("USAGEBAR_TEST") == "1"
                ? TrayIconLayoutSettings.Default
                : settings.IconLayout;
            _view.ShowIcon(IconLayout.Compute(snapshot.Results, iconLayout));
            _view.ShowCards(TooltipCardBuilder.Build(snapshot, settings.BalanceHidingThreshold ?? -1));

            await _notifications.EmitAsync(snapshot.Windows, settings, _shutdown.Token).ConfigureAwait(false);

            _logger.LogInformation(
                "Refresh complete: {ProviderCount} provider(s), {WindowCount} window(s).",
                snapshot.Results.Count,
                snapshot.Windows.Count);
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested — do not reschedule.
            return;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected refresh failure.");
            // Read fresh settings on failure so the timer period stays current.
            try { settings = await _settings.ReadAsync(_shutdown.Token).ConfigureAwait(false); }
            catch { settings = AppSettings.Default; }
        }
        finally
        {
            _refreshGate.Release();
        }

        ScheduleNext(settings.RefreshPeriodMinute, scheduleAnchor);
    }

    /// <summary>
    /// Reschedules the next refresh when we skipped this one due to single-flight. Reads the
    /// current refresh period from settings so the fallback respects user configuration.
    /// </summary>
    private async Task ScheduleNextFallbackAsync(DateTimeOffset? scheduleAnchor)
    {
        try
        {
            var settings = await _settings.ReadAsync().ConfigureAwait(false);
            ScheduleNext(settings.RefreshPeriodMinute, scheduleAnchor);
        }
        catch
        {
            ScheduleNext(AppSettings.Default.RefreshPeriodMinute, scheduleAnchor);
        }
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
                var elapsed = _clock.Now - scheduleAnchor.Value;
                dueTime = elapsed >= period ? TimeSpan.Zero : period - elapsed;
            }

            _timer?.Dispose();
            _timer = new Timer(static state => _ = ((UsageRefreshService)state!).RefreshAsync(null), this, dueTime, Timeout.InfiniteTimeSpan);
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

        // Wait for the in-flight refresh to complete before disposing the gate so
        // the finally block never releases a disposed semaphore.
        try { _refreshGate.Wait(TimeSpan.FromSeconds(5)); }
        catch { /* best-effort; shutdown must not throw */ }

        _refreshGate.Dispose();
        _shutdown.Dispose();
    }
}
