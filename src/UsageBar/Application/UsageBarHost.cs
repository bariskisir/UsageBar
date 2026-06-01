using UsageBar.Domain;
using UsageBar.Infrastructure.Configuration;
using UsageBar.Infrastructure.Diagnostics;
using UsageBar.Infrastructure.FileSystem;
using UsageBar.Infrastructure.Startup;
using UsageBar.Providers;
using UsageBar.Shell.Tray;

namespace UsageBar.Application;

internal sealed class UsageBarHost : IDisposable
{
    private readonly AppLogger _logger;
    private readonly HttpClient _httpClient;
    private readonly TrayIcon _trayIcon;
    private readonly WebViewTooltip? _tooltip;
    private readonly RefreshCoordinator _refreshCoordinator;
    private readonly TrayUiSyncContext? _syncContext;
    private bool _disposed;

    private UsageBarHost(
        AppLogger logger,
        HttpClient httpClient,
        TrayIcon trayIcon,
        WebViewTooltip? tooltip,
        RefreshCoordinator refreshCoordinator,
        TrayUiSyncContext? syncContext)
    {
        _logger = logger;
        _httpClient = httpClient;
        _trayIcon = trayIcon;
        _tooltip = tooltip;
        _refreshCoordinator = refreshCoordinator;
        _syncContext = syncContext;

        _trayIcon.RefreshRequested += OnRefreshRequested;
        _trayIcon.ExitRequested += OnExitRequested;
    }

    public static UsageBarHost CreateDefault()
    {
        AppLogger? logger = null;
        HttpClient? httpClient = null;
        TrayIcon? trayIcon = null;
        WebViewTooltip? tooltip = null;
        RefreshCoordinator? refreshCoordinator = null;
        TrayUiSyncContext? syncContext = null;
        SettingsService? settings = null;

        try
        {
            logger = new AppLogger(ApplicationPaths.LogFilePath);
            StartupRegistrationService.EnsureRegistered(logger);

            settings = new SettingsService(ApplicationPaths.SettingsFilePath, logger);
            httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

            // Create the WebView2 tooltip; it will fall back to legacy on failure.
            tooltip = new WebViewTooltip();

            // The tray icon window is needed for the sync context and tooltip init.
            trayIcon = new TrayIcon(tooltip, settings);

            // Install a SynchronizationContext that pumps delegates on the STA
            // message-loop thread. Required for WebView2 async continuations.
            syncContext = new TrayUiSyncContext(trayIcon.Hwnd);
            SynchronizationContext.SetSynchronizationContext(syncContext);

            // Kick off async WebView2 init; the message loop will drive it.
            _ = InitTooltipAsync(tooltip, syncContext);

            refreshCoordinator = new RefreshCoordinator(
                settings,
                logger,
                CreateProviders(httpClient),
                trayIcon,
                tooltip);

            return new UsageBarHost(logger, httpClient, trayIcon, tooltip, refreshCoordinator, syncContext);
        }
        catch
        {
            refreshCoordinator?.Dispose();
            tooltip?.Dispose();
            trayIcon?.Dispose();
            httpClient?.Dispose();
            logger?.Dispose();
            throw;
        }
    }

    private static async Task InitTooltipAsync(WebViewTooltip tooltip, TrayUiSyncContext syncContext)
    {
        var instance = NativeMethods.GetModuleHandle(null);
        await tooltip.InitAsync(instance).ConfigureAwait(true);
    }

    public void Run()
    {
        _refreshCoordinator.Start();
        _trayIcon.RunMessageLoop();
    }

    // Expose the tray HWND so the sync context can be installed from the outside.
    public nint TrayHwnd => _trayIcon.Hwnd;

    private static IUsageProvider[] CreateProviders(HttpClient httpClient)
    {
        return
        [
            new CodexProvider(httpClient),
            new ClaudeProvider(httpClient),
            new DeepSeekProvider(httpClient),
            new OpenRouterProvider(httpClient),
            new DeepgramProvider(httpClient)
        ];
    }

    private void OnRefreshRequested(object? sender, EventArgs eventArgs)
    {
        _refreshCoordinator.TriggerManualRefresh();
    }

    private void OnExitRequested(object? sender, EventArgs eventArgs)
    {
        _refreshCoordinator.Stop();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _trayIcon.RefreshRequested -= OnRefreshRequested;
        _trayIcon.ExitRequested -= OnExitRequested;

        _refreshCoordinator.Dispose();
        _trayIcon.Dispose();
        _tooltip?.Dispose();
        _httpClient.Dispose();
        _logger.Dispose();
    }
}
