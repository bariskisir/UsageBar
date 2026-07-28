using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
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
    private readonly FallbackStatusWindow _fallbackStatusWindow;
    private readonly NativeTooltip _tooltip;
    private readonly IUsageRefreshService _refresh;
    private readonly SettingsPanel _settingsPanel;
    private readonly DesktopApplicationCoordinator _coordinator;
    private readonly IUpdateService _updateService;
    private readonly ILogger<TrayApplication> _logger;
    private readonly CancellationTokenSource _lifetime = new();

    public TrayApplication(
        NativeTray tray,
        FallbackStatusWindow fallbackStatusWindow,
        NativeTooltip tooltip,
        IUsageRefreshService refresh,
        SettingsPanel settingsPanel,
        DesktopApplicationCoordinator coordinator,
        IUpdateService updateService,
        ILogger<TrayApplication> logger)
    {
        _tray = tray;
        _fallbackStatusWindow = fallbackStatusWindow;
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

        if (!_tray.IsStatusNotifierAvailable)
        {
            _logger.LogWarning("No StatusNotifier host was detected; showing the fallback status window.");
            _tooltip.SetTransientFor(_fallbackStatusWindow.Window);
            _settingsPanel.SetTransientFor(_fallbackStatusWindow.Window);
            _fallbackStatusWindow.Show();
        }

        var settings = _coordinator.InitializeAsync(_lifetime.Token).GetAwaiter().GetResult();

        var refreshTask = _refresh.RunAsync(_lifetime.Token);
        var updateTask = settings.Update?.OnStartup ?? true
            ? CheckForUpdatesAsync(_lifetime.Token)
            : Task.CompletedTask;

        _logger.LogInformation("Linux event loop running. Press Ctrl+C to exit.");

        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            RequestShutdown("Ctrl+C pressed");
        };
        Console.CancelKeyPress += cancelHandler;
        using var terminateRegistration = PosixSignalRegistration.Create(
            PosixSignal.SIGTERM,
            context =>
            {
                context.Cancel = true;
                RequestShutdown("SIGTERM received");
            });

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
    }

    private void WireEvents()
    {
        _tray.TooltipToggleRequested += (x, y) =>
        {
            _logger.LogInformation("Tooltip toggle requested.");
            _tooltip.ToggleNearIcon(x, y);
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
            RequestShutdown("Exit requested");
        };

        _fallbackStatusWindow.UsageRequested += () => _tooltip.ShowNearIcon();
        _fallbackStatusWindow.SettingsRequested += () => _settingsPanel.Show();
        _fallbackStatusWindow.RefreshRequested += () => _refresh.RequestManualRefresh();
        _fallbackStatusWindow.ExitRequested += () =>
        {
            RequestShutdown("Exit requested from fallback window");
        };
    }

    private void RequestShutdown(string reason)
    {
        if (_lifetime.IsCancellationRequested)
        {
            return;
        }

        _logger.LogInformation("{ShutdownReason}, shutting down.", reason);
        _lifetime.Cancel();
        Gtk.Application.Invoke((_, _) => Gtk.Application.Quit());
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
