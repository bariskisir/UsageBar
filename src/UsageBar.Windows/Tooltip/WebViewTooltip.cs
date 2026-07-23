using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using System.Reflection;
using System.Text;
using System.Text.Json;
using UsageBar.Core.Application;
using UsageBar.Core.Domain;
using UsageBar.Core.Infrastructure;
using UsageBar.Core.Tooltip;
using UsageBar.Windows.Infrastructure;
using UsageBar.Windows.Tray;

namespace UsageBar.Windows.Tooltip;
/// <summary>
/// Custom tooltip rendered with WebView2. The popup is a borderless, top-most,
/// non-activating window shown on <c>NIN_POPUPOPEN</c> and hidden on <c>NIN_POPUPCLOSE</c>.
/// Content is pushed from the background refresh thread via a posted window message.
/// </summary>
/// <remarks>
/// If WebView2 initialisation fails (missing runtime, COM error, directory access), the
/// popup window is torn down and <see cref = "Hwnd"/> stays 0, so show/push calls become
/// no-ops — the app simply runs without a hover tooltip.
/// </remarks>
internal sealed class WebViewTooltip : IWebViewTooltip, IDisposable
{
    private const int DefaultWidth = 300;
    private const int CornerRadius = 10;
    private const int MinHeight = 60;
    private readonly WebViewEnvironment _webViewEnv;
    private readonly NativeMethods.WndProc _wndProc;
    private readonly WebViewPopupLifecycle _lifecycle;
    private volatile nint _hwnd;
    private CoreWebView2Environment? _env;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;
    private readonly Lock _gate = new();
    private string _pendingJson = """{"cards":[]}""";
    private NativeMethods.Rect _lastIconRect;
    private int _widthCss = DefaultWidth;
    private int _heightCss;
    private volatile bool _visible;
    public WebViewTooltip(WebViewEnvironment webViewEnv, ILogger<WebViewTooltip> logger)
    {
        _webViewEnv = webViewEnv;
        _wndProc = WndProc;
        _lifecycle = new WebViewPopupLifecycle(logger, "Tooltip");
    }

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

        // Size the window at default width before attaching the controller.
        var scale = WindowScale();
        var initialWidth = Scaled(DefaultWidth, scale);
        var initialHeight = Scaled(MinHeight, scale);
        NativeMethods.SetWindowPos(_hwnd, 0, 0, 0, initialWidth, initialHeight, NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOZORDER);
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
            // IPC shim MUST exist before NavigateToString so the page script can signal ready.
            await _core.AddScriptToExecuteOnDocumentCreatedAsync("window.ipc={postMessage:(m)=>window.chrome.webview.postMessage(typeof m==='string'?JSON.parse(m):m)," + "addMessageListener:(cb)=>window.chrome.webview.addEventListener('message',(e)=>cb(e.data))};").ConfigureAwait(true);
            _core.WebMessageReceived += OnWebMessageReceived;
            _core.NavigateToString(ReadHtmlDocument());
            // _navigated is set to true only after the page signals "ready" — see
            // OnWebMessageReceived. Setting it here would race with data pushes that
            // arrive before the DOM and window.__render are available.
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
    public void SetContent(IReadOnlyList<TooltipCard> cards, int scale)
    {
        var json = JsonSerializer.Serialize(new TooltipPayload(cards, scale), TooltipJsonContext.Default.TooltipPayload);
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

        _ = SuspendCore();
        NativeMethods.TrimWorkingSet();
    }

    /// <summary>
    /// Wakes the WebView before showing: makes the controller visible, restores the normal
    /// memory band, resumes the suspended renderer, and re-renders the latest content (updates
    /// pushed while suspended are skipped, so the freshest payload is applied here).
    /// Bumps the suspend version so any in-flight <see cref = "SuspendCore"/> continuation
    /// recognises it is stale and discards its result.
    /// </summary>
    private void ResumeCore()
    {
        _lifecycle.Resume();
        if (_hwnd != 0)
        {
            NativeMethods.PostMessage(_hwnd, NativeMethods.WmTooltipSetData, 0, 0);
        }
    }

    /// <summary>
    /// Frees renderer memory while hidden: hides the controller, drops to the low memory band,
    /// and suspends the renderer. Suspend requires a hidden controller and a completed initial
    /// navigation, so it is a no-op before the page is ready.
    /// Captures the current suspend version before awaiting; if <see cref = "ResumeCore"/> bumps
    /// the version while the async operation is in-flight the continuation discards the stale
    /// result so <c>_suspended</c> never flips back to <see langword="true"/> after a resume.
    /// </summary>
    private Task SuspendCore() => _lifecycle.SuspendAsync();
    public void Dispose()
    {
        if (_lifecycle.IsDisposed)
        {
            return;
        }

        _lifecycle.Dispose();
        // Close and dispose the WebView2 controller first — its child window is hosted
        // inside the popup window and must be torn down before the parent is destroyed.
        _controller = null;
        _core = null;
        _env = null;
        // Now destroy the popup window itself.
        if (_hwnd != 0)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = 0;
        }
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
        if (_lifecycle.IsDisposed || !_lifecycle.IsReady || _core is null)
        {
            return;
        }

        string json;
        lock (_gate)
        {
            json = _pendingJson;
        }

        try
        {
            _core.PostWebMessageAsJson(json);
        }
        catch (Exception)
        {
        // The WebView may be mid-navigation, disposed, or otherwise unavailable.
        // Silently skip this push — the next hover-triggered ResumeCore will
        // re-post the latest payload via WmTooltipSetData.
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        TooltipInboundMessage? message;
        try
        {
            message = JsonSerializer.Deserialize(e.WebMessageAsJson, TooltipJsonContext.Default.TooltipInboundMessage);
        }
        catch (JsonException)
        {
            // Malformed JSON from the WebView — ignore and keep running.
            return;
        }

        if (message is null)
        {
            return;
        }

        switch (message.Type)
        {
            case "ready":
                _lifecycle.MarkReady();
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
                    _ = SuspendCore();
                    NativeMethods.TrimWorkingSet();
                }

                break;
            case "size":
                if (message.Width is> 0)
                {
                    _widthCss = message.Width.Value;
                }

                if (message.Height is { } height)
                {
                    _heightCss = Math.Max(MinHeight, height);
                }

                if (_visible)
                {
                    Reposition();
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

        var iconRect = new LayoutRect(_lastIconRect.Left, _lastIconRect.Top, _lastIconRect.Right, _lastIconRect.Bottom);
        var wa = WorkArea();
        var workArea = new LayoutRect(wa.Left, wa.Top, wa.Right, wa.Bottom);
        var placement = TooltipPlacementCalculator.Compute(iconRect, workArea, _widthCss, _heightCss, MinHeight, CornerRadius, WindowScale());
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, placement.X, placement.Y, placement.Width, placement.Height, NativeMethods.SWP_NOACTIVATE);
        if (_controller is not null)
        {
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, placement.Width, placement.Height);
        }

        // Clear the previous region so the system deletes it, then create and set a
        // new one. This avoids GDI handle accumulation on Windows versions where
        // SetWindowRgn does not reliably free the old region.
        NativeMethods.SetWindowRgn(_hwnd, 0, false);
        var region = NativeMethods.CreateRoundRectRgn(0, 0, placement.Width + 1, placement.Height + 1, placement.CornerRadius * 2, placement.CornerRadius * 2);
        NativeMethods.SetWindowRgn(_hwnd, region, true);
    }

    private nint CreatePopupWindow(nint instance)
    {
        var className = NativeMethods.RegisterWindowClass("UsageBarWebTooltip", _wndProc, instance);
        if (className is null)
        {
            return 0;
        }

        var hwnd = NativeMethods.CreateWindowEx(NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_NOACTIVATE, className, "UsageBarWebTooltip", NativeMethods.WS_POPUP, 0, 0, _widthCss, MinHeight, 0, 0, instance, 0);
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
            // Fall back to the primary monitor dimensions when SystemParametersInfo
            // fails (unlikely, but possible on locked or headless sessions).
            var width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
            var height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);
            if (width > 0 && height > 0)
            {
                return new NativeMethods.Rect
                {
                    Left = 0,
                    Top = 0,
                    Right = width,
                    Bottom = height
                };
            }

            // Absolute last resort — a safe 1080p-ish default.
            return new NativeMethods.Rect
            {
                Left = 0,
                Top = 0,
                Right = 1920,
                Bottom = 1040
            };
        }

        return rect;
    }

    private void TearDownPartialInit()
    {
        _lifecycle.Dispose();
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
        var assembly = typeof(EmbeddedPageLoader).Assembly;
        return EmbeddedPageLoader.Load(assembly, "UsageBar.Core.Frontend.index.html", "UsageBar.Core.Frontend.tooltip.css", "UsageBar.Core.Frontend.tooltip.js", "{{TOOLTIP_CSS}}", "{{TOOLTIP_JS}}").Replace("{{OPENAI_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.OpenAI.openai.svg"), StringComparison.Ordinal).Replace("{{CLAUDE_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Claude.claude.svg"), StringComparison.Ordinal).Replace("{{ELEVENLABS_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.ElevenLabs.elevenlabs.svg"), StringComparison.Ordinal).Replace("{{KILO_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Kilo.kilo.svg"), StringComparison.Ordinal).Replace("{{COPILOT_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Copilot.copilot.svg"), StringComparison.Ordinal).Replace("{{WARP_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Warp.warp.svg"), StringComparison.Ordinal).Replace("{{SYNTHETIC_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Synthetic.synthetic.svg"), StringComparison.Ordinal).Replace("{{CHUTES_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Chutes.chutes.svg"), StringComparison.Ordinal).Replace("{{ZAI_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Zai.zai.svg"), StringComparison.Ordinal).Replace("{{ALIBABA_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Alibaba.alibaba.svg"), StringComparison.Ordinal).Replace("{{MINIMAX_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.MiniMax.minimax.svg"), StringComparison.Ordinal).Replace("{{CODEBUFF_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Codebuff.codebuff.svg"), StringComparison.Ordinal).Replace("{{ANTIGRAVITY_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Antigravity.antigravity.svg"), StringComparison.Ordinal).Replace("{{COMMANDCODE_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.CommandCode.commandcode.svg"), StringComparison.Ordinal);
    }

    private static string ReadSvgDataUri(Assembly assembly, string resourceName)
    {
        var stream = assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' is missing.");
        using (var reader = new StreamReader(stream))
        {
            var bytes = Encoding.UTF8.GetBytes(reader.ReadToEnd());
            return $"data:image/svg+xml;base64,{Convert.ToBase64String(bytes)}";
        }
    }
}