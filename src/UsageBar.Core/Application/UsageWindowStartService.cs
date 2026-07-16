using Microsoft.Extensions.Logging;
using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;

namespace UsageBar.Core.Application;

/// <summary>
/// Starts a session with a minimal request after a usage reset, or when an unused session's
/// reset timestamp moves later between two consecutive observations. The first observation only
/// arms the provider, so enabling the option never consumes the currently active window.
/// </summary>
internal sealed class UsageWindowStartService(
    IWindowStartRequestSender sender,
    ILogger<UsageWindowStartService> logger) : IUsageWindowStartService
{
    private static readonly HashSet<string> SupportedProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Codex",
        "Claude",
        "Antigravity",
    };

    private readonly Dictionary<string, ProviderResetState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task ObserveAsync(
        IReadOnlyList<UsageWindow> windows,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var enabledProviders = (settings.Providers ?? [])
            .Where(provider => provider.Enabled && provider.StartWindowAfterReset == true)
            .Where(provider => SupportedProviders.Contains(provider.Name))
            .GroupBy(provider => provider.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var smallModelSelector = (settings.Models ?? ModelSettings.Default).Normalize().SmallModelSelector;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var disabled in _states.Keys.Where(name => !enabledProviders.ContainsKey(name)).ToArray())
            {
                _states.Remove(disabled);
            }

            foreach (var providerName in enabledProviders.Keys)
            {
                var currentWindows = windows
                    .Where(window => string.Equals(window.ProviderName, providerName, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(window.Label, "Session", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(WindowKey, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

                if (!_states.TryGetValue(providerName, out var state))
                {
                    _states[providerName] = new ProviderResetState(currentWindows);
                    logger.LogDebug("{Provider} reset-window starter armed with {WindowCount} session window(s).", providerName, currentWindows.Count);
                    continue;
                }

                var resetObserved = false;
                var movingResetObserved = false;
                foreach (var current in currentWindows)
                {
                    if (!state.Windows.TryGetValue(current.Key, out var observation))
                    {
                        state.Windows[current.Key] = new WindowObservation(current.Value);
                        continue;
                    }

                    var usageReset = current.Value.UsedPercent < observation.UsedPercent;
                    resetObserved |= usageReset;
                    if (usageReset && current.Value.UsedPercent < 5)
                    {
                        // The regular reset path already warms this low-usage window.
                        observation.LowUsageWarmTriggered = true;
                    }

                    if (current.Value.UsedPercent >= 5)
                    {
                        observation.LowUsageWarmTriggered = false;
                    }
                    else if (current.Value.ResetAt is { } resetAt
                        && observation.ResetAt is { } previousResetAt
                        && resetAt > previousResetAt)
                    {
                        if (!observation.LowUsageWarmTriggered)
                        {
                            observation.LowUsageWarmTriggered = true;
                            movingResetObserved = true;
                        }
                    }
                }

                state.PendingStart |= resetObserved || movingResetObserved;
                if (state.PendingStart)
                {
                    try
                    {
                        await sender.StartAsync(providerName, smallModelSelector, cancellationToken).ConfigureAwait(false);
                        state.PendingStart = false;
                        logger.LogInformation(
                            movingResetObserved && !resetObserved
                                ? "{Provider} session window was warmed after its low-usage reset timestamp moved later."
                                : "{Provider} session window was warmed after reset with a minimal request.",
                            providerName);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "{Provider} session-window start request failed; it will be retried on the next refresh.", providerName);
                    }
                }

                foreach (var current in currentWindows)
                {
                    var observation = state.Windows[current.Key];
                    observation.UsedPercent = current.Value.UsedPercent;
                    observation.ResetAt = current.Value.ResetAt;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string WindowKey(UsageWindow window) => $"{window.Label}|{window.SubLabel}";

    private sealed class ProviderResetState(IReadOnlyDictionary<string, UsageWindow> windows)
    {
        public Dictionary<string, WindowObservation> Windows { get; } = windows.ToDictionary(
            pair => pair.Key,
            pair => new WindowObservation(pair.Value),
            StringComparer.OrdinalIgnoreCase);

        public bool PendingStart { get; set; }
    }

    private sealed class WindowObservation(UsageWindow window)
    {
        public double UsedPercent { get; set; } = window.UsedPercent;

        public DateTimeOffset? ResetAt { get; set; } = window.ResetAt;

        public bool LowUsageWarmTriggered { get; set; }
    }
}
