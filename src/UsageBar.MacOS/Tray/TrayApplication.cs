using AppKit;
using Foundation;
using Microsoft.Extensions.Logging;
using UsageBar.Core.Application;
using UsageBar.Core.Configuration;
using UsageBar.Core.Infrastructure;
using UsageBar.MacOS.Settings;
using UsageBar.MacOS.Tooltip;
using UsageBar.MacOS.Tray;

namespace UsageBar.MacOS;

internal sealed class TrayApplication : IDisposable
{
    private readonly NativeTray _tray;
    private readonly NativeTooltip _tooltip;
    private readonly IUsageRefreshService _refresh;
    private readonly DesktopApplicationCoordinator _coordinator;
    private readonly IUpdateService _updateService;
    private readonly SettingsPanel _settingsPanel;
    private readonly ILogger<TrayApplication> _logger;
    private readonly CancellationTokenSource _lifetime = new();

    public TrayApplication(
        NativeTray tray,
        NativeTooltip tooltip,
        IUsageRefreshService refresh,
        DesktopApplicationCoordinator coordinator,
        IUpdateService updateService,
        SettingsPanel settingsPanel,
        ILogger<TrayApplication> logger)
    {
        _tray = tray;
        _tooltip = tooltip;
        _refresh = refresh;
        _coordinator = coordinator;
        _updateService = updateService;
        _settingsPanel = settingsPanel;
        _logger = logger;
    }

    public void Run()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Accessory;

        _logger.LogInformation("macOS tray application initialising.");
        WireEvents();

        var settings = _coordinator.InitializeAsync(_lifetime.Token).GetAwaiter().GetResult();

        var refreshTask = _refresh.RunAsync(_lifetime.Token);
        var updateTask = settings.Update?.OnStartup ?? true
            ? CheckForUpdatesAsync(_lifetime.Token)
            : Task.CompletedTask;

        _logger.LogInformation("macOS event loop starting.");
        NSApplication.SharedApplication.Run();
        _logger.LogInformation("macOS event loop stopped.");

        _lifetime.Cancel();
        try
        {
            Task.WhenAll(refreshTask, updateTask).GetAwaiter().GetResult();
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
        _settingsPanel.Dispose();
    }

    private void WireEvents()
    {
        _tray.SettingsRequested += () =>
        {
            _settingsPanel.Show();
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

    private async Task CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        var result = await _updateService.CheckAsync(cancellationToken).ConfigureAwait(false);
        if (result.HasUpdate)
        {
            _tray.ShowNotification(
                UsageBar.Core.Domain.NotificationLevel.High,
                $"Usage Bar {result.LatestVersion} available. Open Settings to download.");
        }
    }
}