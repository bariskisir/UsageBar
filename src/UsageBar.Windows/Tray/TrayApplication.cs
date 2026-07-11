using Microsoft.Extensions.Logging;
using System.Diagnostics;
using UsageBar.Core.Application;
using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;
using UsageBar.Core.Infrastructure;
using UsageBar.Windows.Infrastructure;
using UsageBar.Windows.Settings;
using UsageBar.Windows.Tooltip;

namespace UsageBar.Windows.Tray;

internal static class UpdateUrls
{
    public const string LatestRelease = "https://github.com/bariskisir/usagebar/releases/latest";
}

/// <summary>
/// Composition root for the running tray app: registers startup, installs the STA
/// synchronisation context, starts the WebView2 tooltip and the refresh loop, wires tray
/// events to the refresh service, manages update checking, and pumps the Win32 message loop
/// until exit.
/// </summary>
internal sealed class TrayApplication(
    ITrayIconWindow window,
    IWebViewTooltip tooltip,
    ITrayContextMenu contextMenu,
    IUsageRefreshService refresh,
    DesktopApplicationCoordinator coordinator,
    IUpdateService updateService,
    SettingsPanel settingsPanel,
    ILogger<TrayApplication> logger)
{
    private readonly CancellationTokenSource _lifetime = new();

    public void Run()
    {
        logger.LogInformation("Tray application initialising.");
        WireEvents();

        var currentSettings = coordinator.InitializeAsync(_lifetime.Token).GetAwaiter().GetResult();

        // Install a SynchronizationContext that pumps continuations on this STA message-loop
        // thread; required for WebView2 async continuations.
        SynchronizationContext.SetSynchronizationContext(new TrayUiSyncContext(window.Hwnd));

        var tooltipTask = InitTooltipAsync();
        var settingsPanelTask = InitSettingsPanelAsync();
        var refreshTask = refresh.RunAsync(_lifetime.Token);

        Task? updateTask = null;
        if (currentSettings.Update?.OnStartup ?? true)
        {
            updateTask = CheckForUpdatesAsync(silent: true, _lifetime.Token);
        }

        logger.LogInformation("Tray message loop starting.");
        window.RunMessageLoop();
        logger.LogInformation("Tray message loop stopped; cancelling application services.");

        _lifetime.Cancel();
        var tasks = updateTask is null
            ? new[] { tooltipTask, settingsPanelTask, refreshTask }
            : new[] { tooltipTask, settingsPanelTask, refreshTask, updateTask };
        try
        {
            Task.WhenAll(tasks).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Expected during normal shutdown.
        }
    }

    private void WireEvents()
    {
        window.TooltipShowRequested += OnTooltipShowRequested;
        window.TooltipHideRequested += tooltip.Hide;
        contextMenu.RefreshRequested += refresh.RequestManualRefresh;
        contextMenu.ExitRequested += OnExitRequested;
        contextMenu.SettingsRequested += () => settingsPanel.Show();
    }

    private void OnTooltipShowRequested(NativeMethods.Rect? iconRect, int fallbackX, int fallbackY) =>
        tooltip.ShowNearIcon(iconRect, fallbackX, fallbackY);

    private void OnExitRequested()
    {
        logger.LogInformation("Exit requested from tray context menu.");
        _lifetime.Cancel();
        window.Quit();
    }

    private async Task InitTooltipAsync()
    {
        try
        {
            var instance = NativeMethods.GetModuleHandle(null);
            if (!await tooltip.InitAsync(instance).ConfigureAwait(true))
            {
                logger.LogWarning("WebView2 tooltip unavailable; running without a hover tooltip.");
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "WebView2 tooltip initialisation failed.");
        }
    }

    private async Task InitSettingsPanelAsync()
    {
        try
        {
            var instance = NativeMethods.GetModuleHandle(null);
            if (!await settingsPanel.InitAsync(instance).ConfigureAwait(true))
            {
                logger.LogWarning("WebView2 settings panel unavailable.");
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "WebView2 settings panel initialisation failed.");
        }
    }

    private async Task CheckForUpdatesAsync(bool silent = false, CancellationToken cancellationToken = default)
    {
        var result = await updateService.CheckAsync(cancellationToken).ConfigureAwait(true);

        if (result.HasUpdate)
        {
            var message = $"Usage Bar {result.LatestVersion} available — click to download";
            window.ShowBalloon(NotificationLevel.High, message, OpenUpdateUrl);
        }
        else if (!silent)
        {
            if (result.ErrorMessage is not null)
            {
                logger.LogInformation("Update check: {Error}", result.ErrorMessage);
                window.ShowBalloon(NotificationLevel.Reset, $"Update check failed: {result.ErrorMessage}");
            }
            else
            {
                window.ShowBalloon(NotificationLevel.Reset, $"Usage Bar is up to date ({result.LatestVersion}).");
            }
        }
    }

    private static void OpenUpdateUrl()
    {
        try
        {
            Process.Start(new ProcessStartInfo(UpdateUrls.LatestRelease) { UseShellExecute = true });
        }
        catch { }
    }
}