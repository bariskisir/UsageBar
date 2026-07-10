using AppKit;
using Foundation;
using Microsoft.Extensions.Logging;
using UsageBar.Core.Application;
using UsageBar.Core.Configuration;
using UsageBar.Core.Infrastructure;
using UsageBar.MacOS.Tooltip;
using UsageBar.MacOS.Tray;

namespace UsageBar.MacOS;

internal sealed class TrayApplication : IDisposable
{
    private readonly NativeTray _tray;
    private readonly NativeTooltip _tooltip;
    private readonly IUsageRefreshService _refresh;
    private readonly IStartupRegistrationService _startupRegistration;
    private readonly IUpdateService _updateService;
    private readonly ISettingsStore _settingsStore;
    private readonly ProviderInitializer _providerInitializer;
    private readonly ILogger<TrayApplication> _logger;
    private readonly CancellationTokenSource _lifetime = new();

    public TrayApplication(
        NativeTray tray,
        NativeTooltip tooltip,
        IUsageRefreshService refresh,
        IStartupRegistrationService startupRegistration,
        IUpdateService updateService,
        ISettingsStore settingsStore,
        ProviderInitializer providerInitializer,
        ILogger<TrayApplication> logger)
    {
        _tray = tray;
        _tooltip = tooltip;
        _refresh = refresh;
        _startupRegistration = startupRegistration;
        _updateService = updateService;
        _settingsStore = settingsStore;
        _providerInitializer = providerInitializer;
        _logger = logger;
    }

    public void Run()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Accessory;

        _logger.LogInformation("macOS tray application initialising.");
        _providerInitializer.EnsureProviders();
        WireEvents();

        var settings = _settingsStore.Read();
        ApplyStartup(settings);

        var refreshTask = _refresh.RunAsync(_lifetime.Token);

        _logger.LogInformation("macOS event loop starting.");
        NSApplication.SharedApplication.Run();
        _logger.LogInformation("macOS event loop stopped.");

        _lifetime.Cancel();
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
    }

    private void WireEvents()
    {
        _tray.SettingsRequested += () =>
        {
            // Settings panel launch on main thread.
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
            NSApplication.SharedApplication.Terminate(null);
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
