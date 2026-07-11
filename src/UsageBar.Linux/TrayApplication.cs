using Microsoft.Extensions.Logging;
using UsageBar.Core.Application;
using UsageBar.Core.Configuration;
using UsageBar.Core.Infrastructure;
using UsageBar.Linux.Settings;
using UsageBar.Linux.Tooltip;
using UsageBar.Linux.Tray;

namespace UsageBar.Linux;

internal sealed class TrayApplication : IDisposable
{
    private readonly NativeTray _tray;
    private readonly NativeTooltip _tooltip;
    private readonly IUsageRefreshService _refresh;
    private readonly SettingsPanel _settingsPanel;
    private readonly DesktopApplicationCoordinator _coordinator;
    private readonly IUpdateService _updateService;
    private readonly ILogger<TrayApplication> _logger;
    private readonly CancellationTokenSource _lifetime = new();

    public TrayApplication(
        NativeTray tray,
        NativeTooltip tooltip,
        IUsageRefreshService refresh,
        SettingsPanel settingsPanel,
        DesktopApplicationCoordinator coordinator,
        IUpdateService updateService,
        ILogger<TrayApplication> logger)
    {
        _tray = tray;
        _tooltip = tooltip;
        _refresh = refresh;
        _settingsPanel = settingsPanel;
        _coordinator = coordinator;
        _updateService = updateService;
        _logger = logger;
    }

    public void Run()
    {
        _logger.LogInformation("Linux tray application initialising.");
        WireEvents();

        var settings = _coordinator.InitializeAsync(_lifetime.Token).GetAwaiter().GetResult();

        var refreshTask = _refresh.RunAsync(_lifetime.Token);
        var updateTask = settings.Update?.OnStartup ?? true
            ? CheckForUpdatesAsync(_lifetime.Token)
            : Task.CompletedTask;

        _logger.LogInformation("Linux event loop running. Press Ctrl+C to exit.");

        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            _logger.LogInformation("Ctrl+C pressed, shutting down.");
            _lifetime.Cancel();
            Gtk.Application.Invoke((_, _) => Gtk.Application.Quit());
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            Gtk.Application.Run();
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            _lifetime.Cancel();
        }

        _logger.LogInformation("Linux event loop stopped.");
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
            Gtk.Application.Quit();
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