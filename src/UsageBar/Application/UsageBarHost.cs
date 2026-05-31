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
    private readonly RefreshCoordinator _refreshCoordinator;
    private bool _disposed;

    private UsageBarHost(
        AppLogger logger,
        HttpClient httpClient,
        TrayIcon trayIcon,
        RefreshCoordinator refreshCoordinator)
    {
        _logger = logger;
        _httpClient = httpClient;
        _trayIcon = trayIcon;
        _refreshCoordinator = refreshCoordinator;

        _trayIcon.RefreshRequested += OnRefreshRequested;
        _trayIcon.ExitRequested += OnExitRequested;
    }

    public static UsageBarHost CreateDefault()
    {
        AppLogger? logger = null;
        HttpClient? httpClient = null;
        TrayIcon? trayIcon = null;
        RefreshCoordinator? refreshCoordinator = null;

        try
        {
            logger = new AppLogger(ApplicationPaths.LogFilePath);
            StartupRegistrationService.EnsureRegistered(logger);

            var settings = new SettingsService(ApplicationPaths.SettingsFilePath, logger);
            httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

            trayIcon = new TrayIcon();
            refreshCoordinator = new RefreshCoordinator(
                settings,
                logger,
                CreateProviders(httpClient),
                trayIcon);

            return new UsageBarHost(logger, httpClient, trayIcon, refreshCoordinator);
        }
        catch
        {
            refreshCoordinator?.Dispose();
            trayIcon?.Dispose();
            httpClient?.Dispose();
            logger?.Dispose();
            throw;
        }
    }

    public void Run()
    {
        _refreshCoordinator.Start();
        _trayIcon.RunMessageLoop();
    }

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
        _httpClient.Dispose();
        _logger.Dispose();
    }
}
