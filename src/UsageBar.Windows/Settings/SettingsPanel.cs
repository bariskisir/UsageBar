using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.Text.Json;
using UsageBar.Core.Application;
using UsageBar.Core.Domain;
using UsageBar.Core.Infrastructure;
using UsageBar.Core.Settings;
using UsageBar.Windows.Infrastructure;
using UsageBar.Windows.Tooltip;
using UsageBar.Windows.Tray;

namespace UsageBar.Windows.Settings;

internal sealed class SettingsPanel : IDisposable
{
    private const int DefaultWidth = 506;
    private const int DefaultHeight = 540;
    private const int CornerRadius = 10;

    private readonly WebViewEnvironment _webViewEnv;
    private readonly SettingsController _controllerService;
    private readonly ITrayIconWindow _trayWindow;
    private readonly ILogger<SettingsPanel> _logger;
    private readonly NativeMethods.WndProc _wndProc;
    private readonly WebViewPopupLifecycle _lifecycle;

    private nint _hwnd;
    private CoreWebView2Environment? _env;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;

    public SettingsPanel(
        WebViewEnvironment webViewEnv,
        SettingsController controllerService,
        ITrayIconWindow trayWindow,
        ILogger<SettingsPanel> logger)
    {
        _webViewEnv = webViewEnv;
        _controllerService = controllerService;
        _logger = logger;
        _trayWindow = trayWindow;
        _wndProc = WndProc;
        _lifecycle = new WebViewPopupLifecycle(logger, "Settings");
    }

    public nint Hwnd => _hwnd;

    public async Task<bool> InitAsync(nint instance)
    {
        _hwnd = CreatePopupWindow(instance);
        if (_hwnd == 0)
        {
            return false;
        }

        var scale = WindowScale();
        var initialWidth = Scaled(DefaultWidth, scale);
        var initialHeight = Scaled(DefaultHeight, scale);
        NativeMethods.SetWindowPos(
            _hwnd, 0, 0, 0, initialWidth, initialHeight,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOZORDER);

        try
        {
            _env = await _webViewEnv.GetAsync().ConfigureAwait(true);

            _controller = await _env.CreateCoreWebView2ControllerAsync(_hwnd).ConfigureAwait(true);
            _core = _controller.CoreWebView2;
            _lifecycle.Attach(_controller);
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, initialWidth, initialHeight);
            _controller.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1c, 0x1c, 0x1e);
            _core.Settings.AreDevToolsEnabled = false;
            _core.Settings.AreDefaultContextMenusEnabled = false;
            _core.Settings.IsStatusBarEnabled = false;
            _core.Settings.IsZoomControlEnabled = false;

            await _core.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.ipc={postMessage:(m)=>window.chrome.webview.postMessage(typeof m==='string'?JSON.parse(m):m)," +
                "addMessageListener:(cb)=>window.chrome.webview.addEventListener('message',(e)=>cb(e.data))};").ConfigureAwait(true);

            _core.WebMessageReceived += OnWebMessageReceived;
            _core.NavigateToString(ReadSettingsHtml());
            return true;
        }
        catch
        {
            TearDownPartialInit();
            return false;
        }
    }

    public void Show()
    {
        if (_hwnd == 0 || !_lifecycle.IsReady)
        {
            return;
        }

        ResumeCore();
        _ = PushSettingsPayloadAsync();
        CenterWindow();
        NativeMethods.SetWindowPos(_hwnd, 0, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER);
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOW);
        NativeMethods.SetForegroundWindow(_hwnd);
    }

    public void Hide()
    {
        if (_hwnd != 0)
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
        }

        _ = SuspendCore();
        NativeMethods.TrimWorkingSet();
    }

    private void ResumeCore() => _lifecycle.Resume();

    private Task SuspendCore() => _lifecycle.SuspendAsync();

    public void Dispose()
    {
        if (_lifecycle.IsDisposed)
        {
            return;
        }

        _lifecycle.Dispose();
        _controller = null;
        _core = null;
        _env = null;
        if (_hwnd != 0) { NativeMethods.DestroyWindow(_hwnd); _hwnd = 0; }
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case NativeMethods.WmRunDelegate: TrayUiSyncContext.DrainCurrent(); return 0;
            case NativeMethods.WmDestroy: return 0;
        }
        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            await HandleWebMessageAsync(e.WebMessageAsJson).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Windows settings command failed.");
            PostStatus(new SettingsStatusMessage("settings-error", "The command could not be completed."));
        }
    }

    private async Task HandleWebMessageAsync(string json)
    {
        SettingsInboundMessage? message;
        try
        {
            message = JsonSerializer.Deserialize(json, SettingsIpcJsonContext.Default.SettingsInboundMessage);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Settings WebView sent malformed JSON.");
            return;
        }

        if (message is null)
        {
            return;
        }

        _logger.LogDebug("Settings WebView command received: {CommandType}.", message.Type);
        switch (message.Type)
        {
            case "ready":
                _lifecycle.MarkReady();
                await PushSettingsPayloadAsync().ConfigureAwait(true);
                _ = SuspendCore();
                NativeMethods.TrimWorkingSet();
                break;
            case "settings-save": await HandleSettingsSaveAsync(message).ConfigureAwait(true); break;
            case "close": Hide(); break;
            case "drag": HandleDrag(message); break;
            case "test-notification": await _controllerService.SendTestNotificationAsync().ConfigureAwait(true); break;
            case "test-start-window" when message.Settings is not null:
                var testResult = await _controllerService.TestStartWindowAsync(message.Settings).ConfigureAwait(true);
                PostStatus(new SettingsStatusMessage("start-window-test-result", testResult));
                break;
            case "check-update": _ = HandleCheckUpdate(); break;
        }
    }

    private async Task HandleSettingsSaveAsync(SettingsInboundMessage message)
    {
        if (message.Settings is not { } settings)
        {
            return;
        }

        try
        {
            await _controllerService.SaveAsync(settings, message.EnvironmentSourcedKeys).ConfigureAwait(true);
            PostStatus(new SettingsStatusMessage("settings-saved"));
            _logger.LogInformation("Settings command saved successfully.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Save: jsonException");
            PostStatus(new SettingsStatusMessage("settings-error", "The settings are invalid."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Save: unexpectedException");
            PostStatus(new SettingsStatusMessage("settings-error", "Settings could not be saved."));
        }
    }

    private void HandleDrag(SettingsInboundMessage message)
    {
        if (message.DeltaX is not { } dx || message.DeltaY is not { } dy)
        {
            return;
        }

        if (_hwnd == 0)
        {
            return;
        }

        NativeMethods.GetWindowRect(_hwnd, out var rect);
        NativeMethods.SetWindowPos(_hwnd, 0, rect.Left + dx, rect.Top + dy, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private async Task HandleCheckUpdate()
    {
        try
        {
            var result = await _controllerService.CheckForUpdatesAsync().ConfigureAwait(true);

            if (result.HasUpdate)
            {
                var message = $"Usage Bar {result.LatestVersion} available - click to download";
                _trayWindow.ShowBalloon(NotificationLevel.High, message, OpenUpdateUrl);
            }
            else
            {
                var text = result.ErrorMessage is not null
                    ? $"Update check failed: {result.ErrorMessage}"
                    : $"Up to date ({result.LatestVersion})";
                PostStatus(new SettingsStatusMessage("update-result", text));
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Settings: checkUpdate failed"); }
    }

    private void OpenUpdateUrl()
    {
        try
        {
            Process.Start(new ProcessStartInfo(UpdateUrls.LatestRelease) { UseShellExecute = true });
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Settings: openUpdateUrl failed"); }
    }

    private async Task PushSettingsPayloadAsync()
    {
        try
        {
            var message = await _controllerService.GetStateAsync().ConfigureAwait(true);
            var json = JsonSerializer.Serialize(message, SettingsIpcJsonContext.Default.SettingsStateMessage);
            PostJson(json);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Settings state could not be loaded.");
        }
    }

    private void PostStatus(SettingsStatusMessage message)
    {
        var json = JsonSerializer.Serialize(message, SettingsIpcJsonContext.Default.SettingsStatusMessage);
        PostJson(json);
    }

    private void PostJson(string json)
    {
        if (_core is null || !_lifecycle.IsReady)
        {
            return;
        }

        try
        {
            _core.PostWebMessageAsJson(json);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Settings: postWebMessage failed"); }
    }

    private void CenterWindow()
    {
        if (_hwnd == 0)
        {
            return;
        }

        var rect = new NativeMethods.Rect();
        var ok = NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETWORKAREA, 0, ref rect, 0);
        if (!ok || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
        {
            rect = new NativeMethods.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };
        }

        var scale = WindowScale();
        var width = Scaled(DefaultWidth, scale);
        var height = Scaled(DefaultHeight, scale);
        var x = rect.Left + (rect.Right - rect.Left - width) / 2;
        var y = rect.Top + (rect.Bottom - rect.Top - height) / 2;

        NativeMethods.SetWindowPos(_hwnd, 0, x, y, width, height, NativeMethods.SWP_NOZORDER);
        if (_controller is not null)
        {
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, width, height);
        }

        NativeMethods.SetWindowRgn(_hwnd, 0, false);
        var region = NativeMethods.CreateRoundRectRgn(0, 0, width + 1, height + 1, CornerRadius * 2, CornerRadius * 2);
        NativeMethods.SetWindowRgn(_hwnd, region, true);
    }

    private nint CreatePopupWindow(nint instance)
    {
        var className = NativeMethods.RegisterWindowClass("UsageBarSettings", _wndProc, instance);
        if (className is null)
        {
            return 0;
        }

        return NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_TOOLWINDOW,
            className, "UsageBarSettings", NativeMethods.WS_POPUP,
            0, 0, DefaultWidth, DefaultHeight, 0, 0, instance, 0);
    }

    private double WindowScale()
    {
        if (_hwnd == 0)
        {
            return 1.0;
        }

        var dpi = NativeMethods.GetDpiForWindow(_hwnd);
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    private static int Scaled(int value, double scale) => (int)Math.Round(value * scale);

    private void TearDownPartialInit()
    {
        _lifecycle.Dispose();
        _controller = null; _core = null;
        _env = null;
        if (_hwnd != 0) { NativeMethods.DestroyWindow(_hwnd); _hwnd = 0; }
    }

    private static string ReadSettingsHtml()
    {
        var assembly = typeof(EmbeddedPageLoader).Assembly;
        return EmbeddedPageLoader.Load(
            assembly,
            "UsageBar.Core.Frontend.settings.html",
            "UsageBar.Core.Frontend.settings.css",
            "UsageBar.Core.Frontend.settings.js",
            "{{SETTINGS_CSS}}",
            "{{SETTINGS_JS}}");
    }
}
