using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using UsageBar.Application;
using UsageBar.Configuration;
using UsageBar.Domain;
using UsageBar.Infrastructure;
using UsageBar.Tray;

namespace UsageBar.Settings;

internal sealed class SettingsPanel : IDisposable
{
    private const int DefaultWidth = 460;
    private const int DefaultHeight = 540;
    private const int CornerRadius = 10;

    private readonly ISettingsStore _settingsStore;
    private readonly IUsageRefreshService _refresh;
    private readonly IUpdateService _updateService;
    private readonly ITrayIconWindow _trayWindow;
    private readonly NativeMethods.WndProc _wndProc;

    private nint _hwnd;
    private CoreWebView2Environment? _env;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;
    private bool _navigated;
    private bool _disposed;

    public SettingsPanel(
        ISettingsStore settingsStore,
        IUsageRefreshService refresh,
        IUpdateService updateService,
        ITrayIconWindow trayWindow)
    {
        _settingsStore = settingsStore;
        _refresh = refresh;
        _updateService = updateService;
        _trayWindow = trayWindow;
        _wndProc = WndProc;
    }

    public nint Hwnd => _hwnd;

    public async Task<bool> InitAsync(nint instance)
    {
        _hwnd = CreatePopupWindow(instance);
        if (_hwnd == 0) return false;

        var scale = WindowScale();
        var initialWidth = Scaled(DefaultWidth, scale);
        var initialHeight = Scaled(DefaultHeight, scale);
        NativeMethods.SetWindowPos(
            _hwnd, 0, 0, 0, initialWidth, initialHeight,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOZORDER);

        try
        {
            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments =
                    "--disable-gpu --disable-gpu-compositing " +
                    "--renderer-process-limit=1 --disable-renderer-backgrounding " +
                    "--disable-background-timer-throttling " +
                    "--disable-features=Translate,BackForwardCache,MediaRouter,OptimizationHints,AcceptCHFrame",
            };

            var userDataFolder = Path.Combine(ApplicationPaths.WebView2DataDirectory, "Settings");
            _env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder,
                options: options).ConfigureAwait(true);

            _controller = await _env.CreateCoreWebView2ControllerAsync(_hwnd).ConfigureAwait(true);
            _core = _controller.CoreWebView2;
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, initialWidth, initialHeight);
            _controller.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1c, 0x1c, 0x1e);
            _controller.IsVisible = true;

            _core.Settings.AreDevToolsEnabled = false;
            _core.Settings.AreDefaultContextMenusEnabled = false;
            _core.Settings.IsStatusBarEnabled = false;
            _core.Settings.IsZoomControlEnabled = false;

            await _core.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.ipc={postMessage:(m)=>window.chrome.webview.postMessage(m)};").ConfigureAwait(true);

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
        if (_hwnd == 0 || !_navigated) return;

        PushSettingsPayload();
        CenterWindow();
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            0x0001 | 0x0002 | NativeMethods.SWP_NOACTIVATE);
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        NativeMethods.SetForegroundWindow(_hwnd);
    }

    public void Hide()
    {
        if (_hwnd != 0) NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller?.Close();
        (_controller as IDisposable)?.Dispose();
        _controller = null;
        _core = null;
        (_env as IDisposable)?.Dispose();
        _env = null;
        if (_hwnd != 0) { NativeMethods.DestroyWindow(_hwnd); _hwnd = 0; }
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case NativeMethods.WmRunDelegate: TrayUiSyncContext.DrainCurrent(); return 0;
            case 0x0006: return 0;
            case NativeMethods.WmDestroy: return 0;
        }
        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var body = e.TryGetWebMessageAsString();
        if (string.IsNullOrEmpty(body)) return;

        JsonDocument document;
        try { document = JsonDocument.Parse(body); }
        catch (JsonException) { return; }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("type", out var typeProperty)) return;

            switch (typeProperty.GetString())
            {
                case "ready": _navigated = true; PushSettingsPayload(); break;
                case "settings-save": HandleSettingsSave(document.RootElement); break;
                case "close": Hide(); break;
                case "test-notification": _refresh.SendTestNotification(); break;
                case "check-update": _ = HandleCheckUpdate(); break;
            }
        }
    }

    private void HandleSettingsSave(JsonElement root)
    {
        if (!root.TryGetProperty("settings", out var settingsElement)) return;

        try
        {
            var settings = JsonSerializer.Deserialize(
                settingsElement.GetRawText(),
                SettingsJsonContext.Default.AppSettings);
            if (settings is null) return;

            settings = ApplyEnvSourcedKeys(settings, root);

            var normalized = settings.Normalize();
            _settingsStore.Write(normalized);
            _refresh.TriggerManualRefresh();
            PushToJs("window.__settingsSaved", null);
        }
        catch (JsonException) { }
    }

    private static AppSettings ApplyEnvSourcedKeys(AppSettings settings, JsonElement root)
    {
        if (!root.TryGetProperty("envSourcedKeys", out var envSourcedKeys) ||
            envSourcedKeys.ValueKind != JsonValueKind.Array)
        {
            return settings;
        }

        var envSourcedSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in envSourcedKeys.EnumerateArray())
        {
            var name = key.GetString();
            if (!string.IsNullOrEmpty(name)) envSourcedSet.Add(name);
        }

        if (envSourcedSet.Count == 0 || settings.Providers is null) return settings;

        var updated = new List<ProviderSettings>();
        foreach (var p in settings.Providers)
        {
            if (p.Credential is not null && envSourcedSet.Contains(p.Credential))
            {
                var value = p.ApiKey ?? string.Empty;
                Environment.SetEnvironmentVariable(p.Credential, value, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable(p.Credential, value);
                updated.Add(p with { ApiKey = null });
            }
            else
            {
                updated.Add(p);
            }
        }

        return settings with { Providers = updated };
    }

    private async Task HandleCheckUpdate()
    {
        try
        {
            var result = await _updateService.CheckAsync().ConfigureAwait(true);

            if (result.HasUpdate)
            {
                var message = $"Usage Bar {result.LatestVersion} available — click to download";
                _trayWindow.ShowBalloon(NotificationLevel.High, message, OpenUpdateUrl);
            }
            else
            {
                var text = result.ErrorMessage is not null
                    ? $"Update check failed: {result.ErrorMessage}"
                    : $"Up to date ({result.LatestVersion})";
                var json = System.Text.Json.JsonSerializer.Serialize(
                    new { text },
                    new JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
                PushToJs("window.__updateResult", json);
            }
        }
        catch { }
    }

    private static void OpenUpdateUrl()
    {
        try
        {
            Process.Start(new ProcessStartInfo(UpdateUrls.LatestRelease) { UseShellExecute = true });
        }
        catch { }
    }

    private void PushSettingsPayload()
    {
        var settings = _settingsStore.Read();
        var envApiKeys = new Dictionary<string, string>();

        if (settings.Providers is not null)
        {
            foreach (var p in settings.Providers)
            {
                if (p.Credential is not null && string.IsNullOrWhiteSpace(p.ApiKey))
                {
                    var envValue = Environment.GetEnvironmentVariable(p.Credential);
                    if (!string.IsNullOrWhiteSpace(envValue))
                        envApiKeys[p.Credential] = envValue;
                }
            }
        }

        var settingsJson = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
        var envJson = JsonSerializer.Serialize(envApiKeys);
        var payload = $"{{\"settings\":{settingsJson},\"envApiKeys\":{envJson}}}";
        PushToJs("window.__loadSettings", payload);
    }

    private async void PushToJs(string function, string? jsonPayload)
    {
        if (_core is null || !_navigated) return;
        try
        {
            if (jsonPayload is not null)
            {
                var safeJson = System.Text.Encodings.Web.JavaScriptEncoder.Default.Encode(jsonPayload);
                await _core.ExecuteScriptAsync($"{function} && {function}(JSON.parse(\"{safeJson}\"))");
            }
            else
            {
                await _core.ExecuteScriptAsync($"{function} && {function}()");
            }
        }
        catch { }
    }

    private void CenterWindow()
    {
        if (_hwnd == 0) return;

        var rect = new NativeMethods.Rect();
        var ok = NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETWORKAREA, 0, ref rect, 0);
        if (!ok || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            rect = new NativeMethods.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };

        var scale = WindowScale();
        var width = Scaled(DefaultWidth, scale);
        var height = Scaled(DefaultHeight, scale);
        var x = rect.Left + (rect.Right - rect.Left - width) / 2;
        var y = rect.Top + (rect.Bottom - rect.Top - height) / 2;

        NativeMethods.SetWindowPos(_hwnd, 0, x, y, width, height, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        if (_controller is not null) _controller.Bounds = new System.Drawing.Rectangle(0, 0, width, height);

        NativeMethods.SetWindowRgn(_hwnd, 0, false);
        var region = NativeMethods.CreateRoundRectRgn(0, 0, width + 1, height + 1, CornerRadius * 2, CornerRadius * 2);
        NativeMethods.SetWindowRgn(_hwnd, region, true);
    }

    private nint CreatePopupWindow(nint instance)
    {
        var className = NativeMethods.RegisterWindowClass("UsageBarSettings", _wndProc, instance);
        if (className is null) return 0;

        return NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_NOACTIVATE,
            className, "UsageBarSettings", NativeMethods.WS_POPUP,
            0, 0, DefaultWidth, DefaultHeight, 0, 0, instance, 0);
    }

    private double WindowScale()
    {
        if (_hwnd == 0) return 1.0;
        var dpi = NativeMethods.GetDpiForWindow(_hwnd);
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    private static int Scaled(int value, double scale) => (int)Math.Round(value * scale);

    private void TearDownPartialInit()
    {
        _controller?.Close();
        (_controller as IDisposable)?.Dispose();
        _controller = null; _core = null;
        (_env as IDisposable)?.Dispose();
        _env = null;
        if (_hwnd != 0) { NativeMethods.DestroyWindow(_hwnd); _hwnd = 0; }
    }

    private static string ReadSettingsHtml()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var html = assembly.GetManifestResourceStream("UsageBar.Assets.settings.html")
                   ?? throw new InvalidOperationException("Embedded resource 'UsageBar.Assets.settings.html' is missing.");
        using var reader = new StreamReader(html);
        return reader.ReadToEnd();
    }
}
