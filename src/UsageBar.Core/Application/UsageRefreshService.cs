using Microsoft.Extensions.Logging;
using UsageBar.Configuration;
using UsageBar.Domain;
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
    private readonly IRemoteNotificationService _telegram;
    private readonly ILogger<UsageRefreshService> _logger;
    private static readonly NotificationLevel[] SeverityOrder =
        [NotificationLevel.Critical, NotificationLevel.High, NotificationLevel.Reset];

    private readonly ThresholdNotifier _thresholds = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Lock _timerGate = new();
    private Timer? _timer;
    private bool _stopped;

    public UsageRefreshService(
        IEnumerable<IUsageProvider> providers,
        ISettingsStore settings,
        IUsageView view,
        IClock clock,
        IRemoteNotificationService telegram,
        ILogger<UsageRefreshService> logger)
    {
        _providers = providers.ToArray();
        _settings = settings;
        _view = view;
        _clock = clock;
        _telegram = telegram;
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

    /// <summary>Stops scheduling further refreshes.</summary>
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
        // Single-flight: skip if a refresh is already running.
        if (!await _refreshGate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        var settings = AppSettings.Default;

        try
        {
            settings = await _settings.ReadAsync().ConfigureAwait(false);

            var context = ProviderQueryContext.FromSettings(settings, _clock.Now);
            var snapshot = await UsageAggregator
                .RefreshAsync(_providers, context, _logger, CancellationToken.None)
                .ConfigureAwait(false);

            _view.ShowIcon(IconLayout.Compute(snapshot.Results));
            _view.ShowCards(TooltipCardBuilder.Build(snapshot));

            await EmitThresholdNotifications(snapshot.Windows, settings).ConfigureAwait(false);

            _logger.LogInformation(
                "Refresh complete: {ProviderCount} provider(s), {WindowCount} window(s).",
                snapshot.Results.Count,
                snapshot.Windows.Count);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unexpected refresh failure.");
        }
        finally
        {
            _refreshGate.Release();
        }

        ScheduleNext(settings.RefreshPeriodMinute, scheduleAnchor);
    }

    private async Task EmitThresholdNotifications(IReadOnlyList<UsageWindow> windows, AppSettings settings)
    {
        var notifications = _thresholds.Evaluate(windows, settings);
        if (notifications.Count == 0)
        {
            return;
        }

        foreach (var level in SeverityOrder)
        {
            var lines = notifications.Where(n => n.Level == level).Select(n => n.Message).ToList();
            if (lines.Count == 0)
            {
                continue;
            }

            var message = string.Join(Environment.NewLine, lines);
            _view.Notify(level, message);

            await _telegram
                .SendAsync(level, message, CancellationToken.None)
                .ConfigureAwait(false);
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
        _refreshGate.Dispose();
    }
}
