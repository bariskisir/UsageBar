using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace UsageBar.Core.Application;
/// <summary>Runs initial, scheduled and manual refreshes through one non-overlapping loop.</summary>
public sealed class UsageRefreshService : IUsageRefreshService
{
    private readonly IClock _clock;
    private readonly IRefreshCycleRunner _cycleRunner;
    private readonly ILogger<UsageRefreshService> _logger;
    private readonly Channel<DateTimeOffset> _manualRequests = Channel.CreateUnbounded<DateTimeOffset>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private int _running;
    public UsageRefreshService(IRefreshCycleRunner cycleRunner, IClock clock, ILogger<UsageRefreshService> logger)
    {
        _cycleRunner = cycleRunner;
        _clock = clock;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _running, 1) != 0)
        {
            throw new InvalidOperationException("The refresh loop is already running.");
        }

        _logger.LogInformation("Refresh loop starting.");
        try
        {
            var outcome = await _cycleRunner.RunAsync("initial", cancellationToken).ConfigureAwait(false);
            var scheduleAnchor = _clock.Now;
            while (!cancellationToken.IsCancellationRequested)
            {
                var trigger = await WaitForTriggerAsync(scheduleAnchor, TimeSpan.FromMinutes(outcome.RefreshMinutes), cancellationToken).ConfigureAwait(false);
                outcome = await _cycleRunner.RunAsync(trigger.IsManual ? "manual" : "scheduled", cancellationToken).ConfigureAwait(false);
                scheduleAnchor = trigger.IsManual ? trigger.Anchor : _clock.Now;
                while (_manualRequests.Reader.TryRead(out var queuedAnchor))
                {
                    scheduleAnchor = queuedAnchor;
                    _logger.LogDebug("Manual refresh request was coalesced into the next schedule anchor.");
                }
            }
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
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

    public Task SendTestNotificationAsync(CancellationToken cancellationToken = default) => _cycleRunner.SendTestNotificationAsync(cancellationToken);
    private async Task<RefreshTrigger> WaitForTriggerAsync(DateTimeOffset anchor, TimeSpan period, CancellationToken cancellationToken)
    {
        var elapsed = _clock.Now - anchor;
        var delay = elapsed >= period ? TimeSpan.Zero : elapsed <= TimeSpan.Zero ? period : period - elapsed;
        _logger.LogDebug("Waiting {Delay} for scheduled refresh or a manual request.", delay);
        using (var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
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

    private readonly record struct RefreshTrigger(bool IsManual, DateTimeOffset Anchor);
}