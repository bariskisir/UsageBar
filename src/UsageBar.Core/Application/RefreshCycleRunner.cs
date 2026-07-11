using Microsoft.Extensions.Logging;
using System.Diagnostics;
using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;
using UsageBar.Core.Providers;

namespace UsageBar.Core.Application;
internal sealed class RefreshCycleRunner(IEnumerable<IUsageProvider> providers, ISettingsStore settingsStore, IUsageView view, IClock clock, IProviderQueryContextFactory contextFactory, UsageRefreshOptions options, IUsageAggregator aggregator, IThresholdNotificationDispatcher notifications, ILogger<RefreshCycleRunner> logger) : IRefreshCycleRunner
{
    private readonly IReadOnlyList<IUsageProvider> _providers = providers.ToArray();
    public async Task<RefreshOutcome> RunAsync(string trigger, CancellationToken cancellationToken)
    {
        var refreshId = Guid.NewGuid().ToString("N")[..12];
        var started = Stopwatch.GetTimestamp();
        AppSettings settings = AppSettings.Default;
        using (var scope = logger.BeginScope(new Dictionary<string, object?> { ["RefreshId"] = refreshId, ["Trigger"] = trigger, }))
        {
            logger.LogInformation("Refresh started.");
            try
            {
                settings = await settingsStore.ReadAsync(cancellationToken).ConfigureAwait(false);
                var context = contextFactory.Create(settings, clock.Now);
                var snapshot = await aggregator.RefreshAsync(_providers, context, cancellationToken, settings.Providers).ConfigureAwait(false);
                var iconLayout = options.ForceAutomaticIconLayout ? TrayIconLayoutSettings.Default : settings.Visual?.IconLayout;
                view.ShowIcon(IconLayout.Compute(snapshot.Results, iconLayout));
                var iconKeys = _providers.Where(provider => !string.IsNullOrWhiteSpace(provider.Descriptor.IconKey)).ToDictionary(provider => provider.Descriptor.Name, provider => provider.Descriptor.IconKey, StringComparer.OrdinalIgnoreCase);
                view.ShowCards(TooltipCardBuilder.Build(snapshot, iconKeys), settings.Visual?.Scale ?? 100);
                await notifications.EmitAsync(snapshot.Windows, settings, cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Refresh completed: results={ResultCount}; windows={WindowCount}; durationMs={DurationMs:F1}.", snapshot.Results.Count, snapshot.Windows.Count, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Refresh failed after {DurationMs:F1} ms.", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                try
                {
                    settings = await settingsStore.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    settings = AppSettings.Default;
                }
            }

            var minutes = settings.Refresh?.Minute ?? RefreshSettings.Default.Minute;
            logger.LogDebug("Next refresh period resolved to {RefreshMinutes} minutes.", minutes);
            return new RefreshOutcome(minutes);
        }
    }

    public async Task SendTestNotificationAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        await notifications.SendTestNotificationAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}