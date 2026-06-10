using Microsoft.Extensions.Logging;
using UsageBar.Application;
using UsageBar.Infrastructure;
using UsageBar.Tooltip;

namespace UsageBar.Tray;

/// <summary>
/// Composition root for the running tray app: registers startup, installs the STA
/// synchronisation context, starts the WebView2 tooltip and the refresh loop, wires tray
/// events to the refresh service, and pumps the Win32 message loop until exit.
/// </summary>
internal sealed class TrayApplication(
    ITrayIconWindow window,
    IWebViewTooltip tooltip,
    ITrayContextMenu contextMenu,
    IUsageRefreshService refresh,
    IStartupRegistrationService startupRegistration,
    ILogger<TrayApplication> logger)
{
    public void Run()
    {
        startupRegistration.EnsureRegistered();
        WireEvents();

        // Install a SynchronizationContext that pumps continuations on this STA message-loop
        // thread; required for WebView2 async continuations.
        SynchronizationContext.SetSynchronizationContext(new TrayUiSyncContext(window.Hwnd));

        _ = InitTooltipAsync();
        refresh.Start();
        window.RunMessageLoop();
    }

    private void WireEvents()
    {
        window.TooltipShowRequested += OnTooltipShowRequested;
        window.TooltipHideRequested += tooltip.Hide;
        contextMenu.RefreshRequested += refresh.TriggerManualRefresh;
        contextMenu.ExitRequested += OnExitRequested;
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
}
