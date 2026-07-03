using System.Diagnostics;
using Microsoft.Extensions.Logging;
using UsageBar.Application;
using UsageBar.Configuration;
using UsageBar.Domain;
using UsageBar.Infrastructure;
using UsageBar.Tooltip;
using UsageBar.Settings;

namespace UsageBar.Tray;

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
    IStartupRegistrationService startupRegistration,
    IUpdateService updateService,
    ISettingsStore settingsStore,
    SettingsPanel settingsPanel,
    ProviderInitializer providerInitializer,
    ILogger<TrayApplication> logger)
{
    public void Run()
    {
        startupRegistration.EnsureRegistered();
        providerInitializer.EnsureInitialized();
        WireEvents();

        // Install a SynchronizationContext that pumps continuations on this STA message-loop
        // thread; required for WebView2 async continuations.
        SynchronizationContext.SetSynchronizationContext(new TrayUiSyncContext(window.Hwnd));

        _ = InitTooltipAsync();
        _ = InitSettingsPanelAsync();
        refresh.Start();

        var currentSettings = settingsStore.Read();
        if (currentSettings.Update?.OnStartup ?? true)
        {
            _ = CheckForUpdatesAsync(silent: true);
        }

        window.RunMessageLoop();
    }

    private void WireEvents()
    {
        window.TooltipShowRequested += OnTooltipShowRequested;
        window.TooltipHideRequested += tooltip.Hide;
        contextMenu.RefreshRequested += refresh.TriggerManualRefresh;
        contextMenu.ExitRequested += OnExitRequested;
        contextMenu.SettingsRequested += () => settingsPanel.Show();
    }

    private void OnTooltipShowRequested(NativeMethods.Rect? iconRect, int fallbackX, int fallbackY) =>
        tooltip.ShowNearIcon(iconRect, fallbackX, fallbackY);

    private void OnExitRequested()
    {
        refresh.Stop();
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

    private async Task CheckForUpdatesAsync(bool silent = false)
    {
        var result = await updateService.CheckAsync().ConfigureAwait(true);

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
