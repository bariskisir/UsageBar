using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;
using UsageBar.Core.Providers;

namespace UsageBar.Core.Application;

/// <summary>Runs initial, scheduled and manual refreshes through one non-overlapping loop.</summary>
public sealed class UsageRefreshService : IUsageRefreshService
{
    private readonly IReadOnlyList<IUsageProvider> _providers;
    private readonly ISettingsStore _settings;
    private readonly IUsageView _view;
    private readonly IClock _clock;
    private readonly IProviderQueryContextFactory _contextFactory;
    private readonly UsageRefreshOptions _options;
    private readonly IThresholdNotificationDispatcher _notifications;
    private readonly ILogger<UsageRefreshService> _logger;
    private readonly Channel<DateTimeOffset> _manualRequests = Channel.CreateUnbounded<DateTimeOffset>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private int _running;

    public UsageRefreshService(
        IEnumerable<IUsageProvider> providers,
        ISettingsStore settings,
        IUsageView view,
        IClock clock,
        IProviderQueryContextFactory contextFactory,
        UsageRefreshOptions options,
        IEnumerable<IRemoteNotificationService> remoteServices,
        ILogger<UsageRefreshService> logger)
        : this(providers, settings, view, clock, contextFactory, options, new ThresholdNotificationDispatcher(view, remoteServices), logger)
    {
    }

    internal UsageRefreshService(
        IEnumerable<IUsageProvider> providers,
        ISettingsStore settings,
        IUsageView view,
        IClock clock,
        IProviderQueryContextFactory contextFactory,
        UsageRefreshOptions options,
        IThresholdNotificationDispatcher notifications,
        ILogger<UsageRefreshService> logger)
    {
        _providers = providers.ToArray();
        _settings = settings;
        _view = view;
        _clock = clock;
        _contextFactory = contextFactory;
        _options = options;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _running, 1) != 0)
        {
            throw new InvalidOperationException("The refresh loop is already running.");
        }

        _logger.LogInformation("Refresh loop starting with {ProviderCount} registered providers.", _providers.Count);

        try
        {
            var outcome = await RefreshOnceAsync("initial", cancellationToken).ConfigureAwait(false);
            var scheduleAnchor = _clock.Now;

            while (!cancellationToken.IsCancellationRequested)
            {
                var trigger = await WaitForTriggerAsync(
                        scheduleAnchor,
                        TimeSpan.FromMinutes(outcome.RefreshMinutes),
                        cancellationToken)
                    .ConfigureAwait(false);

                outcome = await RefreshOnceAsync(trigger.IsManual ? "manual" : "scheduled", cancellationToken)
                    .ConfigureAwait(false);
                scheduleAnchor = trigger.IsManual ? trigger.Anchor : _clock.Now;

                while (_manualRequests.Reader.TryRead(out var queuedAnchor))
                {
                    scheduleAnchor = queuedAnchor;
                    _logger.LogDebug("Manual refresh request was coalesced into the next schedule anchor.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Refresh loop cancelled.");
        }
        finally
        {
            Volatile.Write(ref _running, 0);
            _logger.LogInformation("Refresh loop stopped.");
        }
    }

    public void RequestManualRefresh()
    {
        var anchor = _clock.Now;
        if (_manualRequests.Writer.TryWrite(anchor))
        {
            _logger.LogInformation("Manual refresh requested at {RequestedAt}.", anchor);
        }
    }

    public async Task SendTestNotificationAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settings.ReadAsync(cancellationToken).ConfigureAwait(false);
        await _notifications.SendTestNotificationAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RefreshOutcome> RefreshOnceAsync(string trigger, CancellationToken cancellationToken)
    {
        var refreshId = Guid.NewGuid().ToString("N")[..12];
        var started = Stopwatch.GetTimestamp();
        AppSettings settings = AppSettings.Default;

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["RefreshId"] = refreshId,
            ["Trigger"] = trigger,
        });

        _logger.LogInformation("Refresh started.");

        try
        {
            settings = await _settings.ReadAsync(cancellationToken).ConfigureAwait(false);
            var context = _contextFactory.Create(settings, _clock.Now);
            var snapshot = await UsageAggregator
                .RefreshAsync(_providers, context, _logger, cancellationToken, settings.Providers)
                .ConfigureAwait(false);

            var iconLayout = _options.ForceAutomaticIconLayout
                ? TrayIconLayoutSettings.Default
                : settings.Visual?.IconLayout;
            _view.ShowIcon(IconLayout.Compute(snapshot.Results, iconLayout));
            var iconKeys = _providers
                .Where(provider => !string.IsNullOrWhiteSpace(provider.Descriptor.IconKey))
                .ToDictionary(
                    provider => provider.Descriptor.Name,
                    provider => provider.Descriptor.IconKey,
                    StringComparer.OrdinalIgnoreCase);
            _view.ShowCards(TooltipCardBuilder.Build(snapshot, iconKeys));

            await _notifications.EmitAsync(snapshot.Windows, settings, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Refresh completed: results={ResultCount}; windows={WindowCount}; durationMs={DurationMs:F1}.",
                snapshot.Results.Count,
                snapshot.Windows.Count,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Refresh failed after {DurationMs:F1} ms.", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            try
            {
                settings = await _settings.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                settings = AppSettings.Default;
            }
        }

        var minutes = settings.Refresh?.Minute ?? RefreshSettings.Default.Minute;
        _logger.LogDebug("Next refresh period resolved to {RefreshMinutes} minutes.", minutes);
        return new RefreshOutcome(minutes);
    }

    private async Task<RefreshTrigger> WaitForTriggerAsync(
        DateTimeOffset anchor,
        TimeSpan period,
        CancellationToken cancellationToken)
    {
        var elapsed = _clock.Now - anchor;
        var delay = elapsed >= period ? TimeSpan.Zero
            : elapsed <= TimeSpan.Zero ? period
            : period - elapsed;

        _logger.LogDebug("Waiting {Delay} for scheduled refresh or a manual request.", delay);

        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = _clock.DelayAsync(delay, waitCancellation.Token);
        var manualTask = _manualRequests.Reader.ReadAsync(waitCancellation.Token).AsTask();
        var completed = await Task.WhenAny(delayTask, manualTask).ConfigureAwait(false);

        if (completed == manualTask)
        {
            var latest = await manualTask.ConfigureAwait(false);
            while (_manualRequests.Reader.TryRead(out var queued))
            {
                latest = queued;
            }

            waitCancellation.Cancel();
            await IgnoreWaitCancellationAsync(delayTask).ConfigureAwait(false);
            return new RefreshTrigger(IsManual: true, latest);
        }

        await delayTask.ConfigureAwait(false);
        waitCancellation.Cancel();
        await IgnoreWaitCancellationAsync(manualTask).ConfigureAwait(false);
        return new RefreshTrigger(IsManual: false, _clock.Now);
    }

    private static async Task IgnoreWaitCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The losing wait is cancelled after Task.WhenAny.
        }
    }

    private readonly record struct RefreshOutcome(int RefreshMinutes);
    private readonly record struct RefreshTrigger(bool IsManual, DateTimeOffset Anchor);
}
