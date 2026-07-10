using Microsoft.Extensions.Logging;
using UsageBar.Core.Application;
using UsageBar.Core.Configuration;
using UsageBar.Core.Infrastructure;
using UsageBar.Linux.Tooltip;
using UsageBar.Linux.Tray;

namespace UsageBar.Linux;

internal sealed class TrayApplication : IDisposable
{
    private readonly NativeTray _tray;
    private readonly NativeTooltip _tooltip;
    private readonly IUsageRefreshService _refresh;
    private readonly IStartupRegistrationService _startupRegistration;
    private readonly ISettingsStore _settingsStore;
    private readonly ProviderInitializer _providerInitializer;
    private readonly ILogger<TrayApplication> _logger;
    private readonly CancellationTokenSource _lifetime = new();

    public TrayApplication(
        NativeTray tray,
        NativeTooltip tooltip,
        IUsageRefreshService refresh,
        IStartupRegistrationService startupRegistration,
        ISettingsStore settingsStore,
        ProviderInitializer providerInitializer,
        ILogger<TrayApplication> logger)
    {
        _tray = tray;
        _tooltip = tooltip;
        _refresh = refresh;
        _startupRegistration = startupRegistration;
        _settingsStore = settingsStore;
        _providerInitializer = providerInitializer;
        _logger = logger;
    }

    public void Run()
    {
        _logger.LogInformation("Linux tray application initialising.");
        _providerInitializer.EnsureProviders();
        WireEvents();

        var settings = _settingsStore.Read();
        ApplyStartup(settings);

        var refreshTask = _refresh.RunAsync(_lifetime.Token);

        _logger.LogInformation("Linux event loop running. Press Ctrl+C to exit.");

        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            _logger.LogInformation("Ctrl+C pressed, shutting down.");
            _lifetime.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            Task.Delay(Timeout.InfiniteTimeSpan, _lifetime.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Both the tray Exit action and Ctrl+C converge on the lifetime token.
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }

        _logger.LogInformation("Linux event loop stopped.");
        try
        {
            refreshTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        _tooltip.Dispose();
        _tray.Dispose();
    }

    private void WireEvents()
    {
        _tray.TooltipToggleRequested += () =>
        {
            _logger.LogInformation("Tooltip toggle requested.");
            _tooltip.ShowNearIcon();
        };

        _tray.SettingsRequested += () =>
        {
            _logger.LogInformation("Settings requested.");
        };

        _tray.RefreshRequested += () =>
        {
            _refresh.RequestManualRefresh();
        };

        _tray.ExitRequested += () =>
        {
            _logger.LogInformation("Exit requested.");
            _lifetime.Cancel();
        };
    }

    private void ApplyStartup(AppSettings settings)
    {
        if (settings.StartWithSystem ?? true)
        {
            _startupRegistration.Register();
        }
        else
        {
            _startupRegistration.Unregister();
        }
    }
}
