using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using UsageBar.Domain;
using UsageBar.Infrastructure;
using UsageBar.Tray;

namespace UsageBar.Tooltip;

/// <summary>
/// Custom tooltip rendered with WebView2. The popup is a borderless, top-most,
/// non-activating window shown on <c>NIN_POPUPOPEN</c> and hidden on <c>NIN_POPUPCLOSE</c>.
/// Content is pushed from the background refresh thread via a posted window message.
/// </summary>
/// <remarks>
/// If WebView2 initialisation fails (missing runtime, COM error, directory access), the
/// popup window is torn down and <see cref="Hwnd"/> stays 0, so show/push calls become
/// no-ops — the app simply runs without a hover tooltip.
/// </remarks>
internal sealed class WebViewTooltip : IWebViewTooltip, IDisposable
{
    private const int Width = 300;
    private const int CornerRadius = 10;
    private const int MinHeight = 60;

    private readonly NativeMethods.WndProc _wndProc;

    private nint _hwnd;
    private CoreWebView2Environment? _env;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;
    private bool _navigated;

    private readonly Lock _gate = new();
    private string _pendingJson = """{"cards":[]}""";
    private NativeMethods.Rect _lastIconRect;
    private int _heightCss;
    private volatile bool _visible;
    private bool _suspended;
    private bool _disposed;

    public WebViewTooltip() => _wndProc = WndProc;

    /// <summary>The popup HWND (0 if not yet created or init failed).</summary>
    public nint Hwnd => _hwnd;

    /// <summary>
    /// Creates the hidden popup window and WebView2 controller on the STA UI thread.
    /// Returns true on success; on failure tears down and returns false.
    /// </summary>
    public async Task<bool> InitAsync(nint instance)
    {
        _hwnd = CreatePopupWindow(instance);
        if (_hwnd == 0)
        {
            return false;
        }

        // Size the window at WIDTH logical pixels before attaching the controller so the
        // page always lays out at WIDTH CSS px.
        var scale = WindowScale();
        var initialWidth = Scaled(Width, scale);
        var initialHeight = Scaled(MinHeight, scale);
        NativeMethods.SetWindowPos(
            _hwnd, 0, 0, 0, initialWidth, initialHeight,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOZORDER);

        try
        {
            // The tooltip is a tiny static page: no GPU compositing, no background timers,
            // a single renderer. These flags drop the standalone GPU process and trim the
            // renderer's resident footprint.
            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments =
                    "--disable-gpu --disable-gpu-compositing " +
                    "--renderer-process-limit=1 --disable-renderer-backgrounding " +
                    "--disable-background-timer-throttling " +
                    "--disable-features=Translate,BackForwardCache,MediaRouter,OptimizationHints,AcceptCHFrame",
            };

            _env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: ApplicationPaths.WebView2DataDirectory,
                options: options).ConfigureAwait(true);

            _controller = await _env.CreateCoreWebView2ControllerAsync(_hwnd).ConfigureAwait(true);
            _core = _controller.CoreWebView2;
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, initialWidth, initialHeight);
            _controller.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x1c, 0x1c, 0x1e);

            // The popup window starts hidden; keep the controller hidden and in the low
            // memory band until the first hover so the idle footprint stays small.
            _controller.IsVisible = false;
            _core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;

            _core.Settings.AreDevToolsEnabled = false;
            _core.Settings.AreDefaultContextMenusEnabled = false;
            _core.Settings.IsStatusBarEnabled = false;
            _core.Settings.IsZoomControlEnabled = false;

            // IPC shim MUST exist before NavigateToString so the page script can signal ready.
            await _core.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.ipc={postMessage:(m)=>window.chrome.webview.postMessage(m)};").ConfigureAwait(true);

            _core.WebMessageReceived += OnWebMessageReceived;
            _core.NavigateToString(ReadHtmlDocument());
            _navigated = true;
            return true;
        }
        catch (Exception)
        {
            // WebView2RuntimeNotFoundException, COMException, directory access, etc.
            TearDownPartialInit();
            return false;
        }
    }

    /// <summary>
    /// Replaces the tooltip content. Thread-safe; the actual script execution happens on the
    /// UI thread via a posted message.
    /// </summary>
    public void SetContent(IReadOnlyList<TooltipCard> cards)
    {
        var json = JsonSerializer.Serialize(new TooltipPayload(cards), TooltipJsonContext.Default.TooltipPayload);
        lock (_gate)
        {
            _pendingJson = json;
        }

        if (_hwnd != 0)
        {
            NativeMethods.PostMessage(_hwnd, NativeMethods.WmTooltipSetData, 0, 0);
        }
    }

    public void ShowNearIcon(NativeMethods.Rect? iconRect, int fallbackX, int fallbackY)
    {
        if (_hwnd == 0)
        {
            return;
        }

        _lastIconRect = iconRect ?? new NativeMethods.Rect
        {
            Left = fallbackX - 8,
            Top = fallbackY - 8,
            Right = fallbackX + 8,
            Bottom = fallbackY + 8,
        };

        _visible = true;
        ResumeCore();
        Reposition();
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);
    }

    public void Hide()
    {
        _visible = false;
        if (_hwnd != 0)
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
        }

        SuspendCore();
        NativeMethods.TrimWorkingSet();
    }

    /// <summary>
    /// Wakes the WebView before showing: makes the controller visible, restores the normal
    /// memory band, resumes the suspended renderer, and re-renders the latest content (updates
    /// pushed while suspended are skipped, so the freshest payload is applied here).
    /// </summary>
    private void ResumeCore()
    {
        if (_controller is null || _core is null)
        {
            return;
        }

        _controller.IsVisible = true;
        _core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;

        if (_suspended)
        {
            _suspended = false;
            try
            {
                _core.Resume();
            }
            catch (Exception)
            {
                // Resume can race teardown; the popup simply stays whatever it was.
            }
        }

        if (_hwnd != 0)
        {
            NativeMethods.PostMessage(_hwnd, NativeMethods.WmTooltipSetData, 0, 0);
        }
    }

    /// <summary>
    /// Frees renderer memory while hidden: hides the controller, drops to the low memory band,
    /// and suspends the renderer. Suspend requires a hidden controller and a completed initial
    /// navigation, so it is a no-op before the page is ready.
    /// </summary>
    private void SuspendCore()
    {
        if (_controller is null || _core is null || !_navigated || _suspended)
        {
            return;
        }

        _controller.IsVisible = false;
        _core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
        _suspended = true;
        _ = _core.TrySuspendAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_hwnd != 0)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = 0;
        }

        _controller?.Close();
        _controller = null;
        _core = null;
        _env = null;
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case NativeMethods.WmTooltipSetData:
                HandleSetData();
                return 0;

            case NativeMethods.WmRunDelegate:
                TrayUiSyncContext.DrainCurrent();
                return 0;

            case NativeMethods.WmDestroy:
                return 0;
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void HandleSetData()
    {
        if (!_navigated || _core is null || _suspended)
        {
            return;
        }

        string json;
        lock (_gate)
        {
            json = _pendingJson;
        }

        _ = _core.ExecuteScriptAsync($"window.__render && window.__render({json})");
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var body = e.TryGetWebMessageAsString();
        if (string.IsNullOrEmpty(body))
        {
            return;
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("type", out var typeProperty))
        {
            return;
        }

        switch (typeProperty.GetString())
        {
            case "ready":
                if (_visible)
                {
                    if (_hwnd != 0)
                    {
                        NativeMethods.PostMessage(_hwnd, NativeMethods.WmTooltipSetData, 0, 0);
                    }
                }
                else
                {
                    // Hidden at startup: suspend now so the renderer sits in the low band
                    // until the first hover wakes it (ResumeCore renders the latest payload),
                    // then hand the startup churn back to the OS.
                    SuspendCore();
                    NativeMethods.TrimWorkingSet();
                }

                break;

            case "height":
                if (document.RootElement.TryGetProperty("value", out var heightProperty) &&
                    heightProperty.TryGetInt32(out var height))
                {
                    _heightCss = Math.Max(MinHeight, height);
                    if (_visible)
                    {
                        Reposition();
                    }
                }

                break;
        }
    }

    private void Reposition()
    {
        if (_hwnd == 0)
        {
            return;
        }

        var placement = TooltipPlacementCalculator.Compute(
            _lastIconRect,
            WorkArea(),
            Width,
            _heightCss,
            MinHeight,
            CornerRadius,
            WindowScale());

        NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HWND_TOPMOST,
            placement.X,
            placement.Y,
            placement.Width,
            placement.Height,
            NativeMethods.SWP_NOACTIVATE);

        if (_controller is not null)
        {
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, placement.Width, placement.Height);
        }

        var region = NativeMethods.CreateRoundRectRgn(
            0,
            0,
            placement.Width + 1,
            placement.Height + 1,
            placement.CornerRadius * 2,
            placement.CornerRadius * 2);
        NativeMethods.SetWindowRgn(_hwnd, region, true);
    }

    private nint CreatePopupWindow(nint instance)
    {
        var className = NativeMethods.RegisterWindowClass("UsageBarWebTooltip", _wndProc, instance);
        if (className is null)
        {
            return 0;
        }

        var hwnd = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_NOACTIVATE,
            className,
            "UsageBarWebTooltip",
            NativeMethods.WS_POPUP,
            0, 0,
            Width, MinHeight,
            0, 0,
            instance,
            0);

        return hwnd;
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

    private static NativeMethods.Rect WorkArea()
    {
        var rect = new NativeMethods.Rect();
        var ok = NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETWORKAREA, 0, ref rect, 0);
        if (!ok || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
        {
            return new NativeMethods.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };
        }

        return rect;
    }

    private void TearDownPartialInit()
    {
        _controller?.Close();
        _controller = null;
        _core = null;
        _env = null;

        if (_hwnd != 0)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = 0;
        }
    }

    private static string ReadHtmlDocument()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("UsageBar.Assets.index.html")!;
        using var reader = new StreamReader(stream);
        return reader
            .ReadToEnd()
            .Replace("{{OPENAI_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Assets.openai.svg"), StringComparison.Ordinal)
            .Replace("{{CLAUDE_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Assets.claude.svg"), StringComparison.Ordinal);
    }

    private static string ReadSvgDataUri(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var bytes = Encoding.UTF8.GetBytes(reader.ReadToEnd());
        return $"data:image/svg+xml;base64,{Convert.ToBase64String(bytes)}";
    }
}
