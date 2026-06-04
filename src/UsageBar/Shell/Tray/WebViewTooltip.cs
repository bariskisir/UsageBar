using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using UsageBar.Domain;

namespace UsageBar.Shell.Tray;

/// <summary>
/// Custom tooltip rendered with WebView2, mirroring the Rust wry-based popup.
/// The popup is a borderless, top-most, non-activating window. It shows in
/// response to NIN_POPUPOPEN and hides on NIN_POPUPCLOSE. Content is pushed
/// from a background refresh thread via WM_TT_SETDATA.
/// </summary>
internal sealed class WebViewTooltip : IDisposable
{
    private const int Width = 300;
    private const int CornerRadius = 10;
    private const int MinHeight = 60;

    private readonly JsonSerializerOptions _camelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ── Window ────────────────────────────────────────────
    private nint _hwnd;
    private readonly NativeMethods.WndProc _wndProc;

    // ── WebView2 ──────────────────────────────────────────
    private CoreWebView2Environment? _env;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;
    private bool _navigated;

    // ── State (thread-safe) ───────────────────────────────
    private string _pendingJson = """{"cards":[]}""";
    private readonly Lock _gate = new();
    private NativeMethods.Rect _lastIconRect;
    private int _heightCss;
    private volatile bool _visible;
    private bool _disposed;

    /// <summary>
    /// True when WebView2 init failed and the app should fall back to the
    /// native szTip text tooltip.
    /// </summary>
    public bool UseLegacyTooltip { get; private set; }

    /// <summary>
    /// The popup HWND (0 if not yet created or init failed).
    /// </summary>
    public nint Hwnd => _hwnd;

    public WebViewTooltip()
    {
        _wndProc = WndProc;
    }

    // ── Init (must be called on STA UI thread before the message loop) ──

    /// <summary>
    /// Creates the hidden popup window and WebView2 controller.
    /// Returns true on success; on failure sets <see cref="UseLegacyTooltip"/>
    /// and returns false.
    /// </summary>
    public async Task<bool> InitAsync(nint instance)
    {
        // 1. Create the borderless popup window.
        _hwnd = CreatePopupWindow(instance);
        if (_hwnd == 0)
        {
            UseLegacyTooltip = true;
            return false;
        }

        // Size the window at WIDTH logical pixels before attaching the controller
        // so the page always lays out at WIDTH CSS px.
        var scale = WindowScale();
        var initialW = Scaled(Width, scale);
        var initialH = Scaled(MinHeight, scale);
        NativeMethods.SetWindowPos(
            _hwnd, 0, 0, 0, initialW, initialH,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOZORDER);

        try
        {
            // 2. Create the WebView2 environment.
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "UsageBar",
                "WebView2");
            _env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder,
                options: null).ConfigureAwait(true);

            // 3. Create the controller bound to our bare HWND.
            _controller = await _env.CreateCoreWebView2ControllerAsync(_hwnd).ConfigureAwait(true);
            _core = _controller.CoreWebView2;
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, initialW, initialH);
            _controller.IsVisible = true;

            // 4. Harden the surface.
            _core.Settings.AreDevToolsEnabled = false;
            _core.Settings.AreDefaultContextMenusEnabled = false;
            _core.Settings.IsStatusBarEnabled = false;
            _core.Settings.IsZoomControlEnabled = false;

            // 5. IPC shim — MUST be set before NavigateToString so window.ipc
            //    exists when tooltip.js runs its signalReady().
            await _core.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.ipc={postMessage:(m)=>window.chrome.webview.postMessage(m)};")
                .ConfigureAwait(true);

            // 6. Wire host-side receiver before navigation.
            _core.WebMessageReceived += OnWebMessageReceived;

            // 7. Load the HTML page.
            _core.NavigateToString(BuildHtml());
            _navigated = true;
            return true;
        }
        catch (Exception)
        {
            // WebView2RuntimeNotFoundException, COMException, directory access, etc.
            UseLegacyTooltip = true;
            TearDownPartialInit();
            return false;
        }
    }

    // ── Content push (thread-safe) ────────────────────────

    /// <summary>
    /// Replaces the tooltip content. Thread-safe; the actual ExecuteScriptAsync
    /// happens on the UI thread via a posted message.
    /// </summary>
    public void SetContent(IReadOnlyList<TooltipCard> cards)
    {
        var json = JsonSerializer.Serialize(new { cards }, _camelCase);
        lock (_gate)
        {
            _pendingJson = json;
        }

        if (_hwnd != 0)
        {
            NativeMethods.PostMessage(_hwnd, NativeMethods.WmTooltipSetData, 0, 0);
        }
    }

    // ── Show / Hide (UI thread) ───────────────────────────

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
            Bottom = fallbackY + 8
        };

        _visible = true;
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
    }

    // ── IDisposable ───────────────────────────────────────

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

    // ── Window procedure (UI thread) ──────────────────────

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case NativeMethods.WmTooltipSetData:
                HandleSetData();
                return 0;

            case NativeMethods.WmRunDelegate:
                if (SynchronizationContext.Current is TrayUiSyncContext ctx)
                {
                    ctx.Drain();
                }
                return 0;

            case NativeMethods.WmDestroy:
                return 0;
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void HandleSetData()
    {
        if (!_navigated || _core is null)
        {
            return;
        }

        string json;
        lock (_gate)
        {
            json = _pendingJson;
        }

        _ = _core.ExecuteScriptAsync(
            $"window.__render && window.__render({json})");
    }

    // ── WebView2 IPC (JS → host, on UI thread) ────────────

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var body = e.TryGetWebMessageAsString();
        if (string.IsNullOrEmpty(body))
        {
            return;
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("type", out var typeProp))
        {
            return;
        }

        var type = typeProp.GetString();
        switch (type)
        {
            case "ready":
                // Page finished loading — push the current snapshot.
                if (_hwnd != 0)
                {
                    NativeMethods.PostMessage(_hwnd, NativeMethods.WmTooltipSetData, 0, 0);
                }
                break;

            case "height":
                if (root.TryGetProperty("value", out var hProp) && hProp.TryGetInt32(out var h))
                {
                    _heightCss = Math.Max(MinHeight, h);
                    if (_visible)
                    {
                        Reposition();
                    }
                }
                break;
        }
    }

    // ── Positioning ───────────────────────────────────────

    private void Reposition()
    {
        if (_hwnd == 0)
        {
            return;
        }

        var scale = WindowScale();
        var heightCss = Math.Max(_heightCss, MinHeight);
        var w = Scaled(Width, scale);
        var h = Scaled(heightCss, scale);
        var gap = Scaled(8, scale);
        var radius = Scaled(CornerRadius, scale);

        var icon = _lastIconRect;
        var work = WorkArea();

        // Anchor above the tray icon.
        var x = icon.Right - w;
        var y = icon.Top - h - gap;
        if (y < work.Top)
        {
            y = icon.Bottom + gap;
        }

        // Clamp to work area.
        if (x + w > work.Right)
        {
            x = work.Right - w - 4;
        }
        if (x < work.Left)
        {
            x = work.Left + 4;
        }
        if (y + h > work.Bottom)
        {
            y = work.Bottom - h - 4;
        }
        if (y < work.Top)
        {
            y = work.Top + 4;
        }

        NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HWND_TOPMOST,
            x, y, w, h,
            NativeMethods.SWP_NOACTIVATE);

        // Sync controller bounds to the host window.
        if (_controller is not null)
        {
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, w, h);
        }

        // Rounded corners — region ownership transfers to the window.
        var region = NativeMethods.CreateRoundRectRgn(0, 0, w + 1, h + 1, radius, radius);
        NativeMethods.SetWindowRgn(_hwnd, region, true);
    }

    // ── Helpers ───────────────────────────────────────────

    private nint CreatePopupWindow(nint instance)
    {
        var className = $"UsageBarWebTooltip-{Guid.NewGuid():N}";

        var wndClass = new NativeMethods.WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WndClassEx>(),
            lpfnWndProc = _wndProc,
            hInstance = instance,
            lpszClassName = className
        };

        if (NativeMethods.RegisterClassEx(ref wndClass) == 0)
        {
            return 0;
        }

        return NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_NOACTIVATE,
            className,
            "UsageBarWebTooltip",
            NativeMethods.WS_POPUP,
            0, 0,
            Width, MinHeight,
            0, 0,
            instance,
            0);
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

    private static int Scaled(int value, double scale)
    {
        return (int)Math.Round(value * scale);
    }

    private static NativeMethods.Rect WorkArea()
    {
        var rect = new NativeMethods.Rect();
        var ok = NativeMethods.SystemParametersInfo(
            NativeMethods.SPI_GETWORKAREA, 0, ref rect, 0);
        if (!ok || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
        {
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
        if (_controller is not null)
        {
            _controller.Close();
            _controller = null;
        }

        _core = null;
        _env = null;

        if (_hwnd != 0)
        {
            NativeMethods.DestroyWindow(_hwnd);
            _hwnd = 0;
        }
    }

    private static void DrainSyncContext()
    {
        if (SynchronizationContext.Current is TrayUiSyncContext ctx)
        {
            ctx.Drain();
        }
    }

    // ── HTML builder ───────────────────────────────────────

    private static string BuildHtml()
    {
        var css = ReadEmbeddedResource("UsageBar.Assets.usagebar.css");
        var js = ReadEmbeddedResource("UsageBar.Assets.tooltip.js");

        const string overrideCss =
            """
            html,body{margin:0;padding:0;overflow:hidden;background:var(--app-bg);}
            #root{width:100%;}
            .menu-surface--tray{box-shadow:none;}
            .menu-surface--tray .menu-stack__item:has(.menu-card--balance){padding:1px 0;}
            .menu-surface--tray .menu-card--balance{padding-top:3px;padding-bottom:3px;gap:0;}
            .menu-surface--tray .menu-card--balance .menu-card__name{font-size:12px;font-weight:500;}
            .menu-surface--tray .menu-card--balance .menu-card__email{font-size:12px;}
            .menu-surface--tray .menu-card__metrics{display:flex;flex-direction:column;gap:10px;}
            .menu-surface--tray .menu-card__plan-inline{margin-left:auto;font-size:11px;font-weight:500;color:var(--text-secondary);white-space:nowrap;}
            """;

        return $$"""
            <!doctype html><html data-theme="dark"><head><meta charset="utf-8">
            <style>{{css}}</style><style>{{overrideCss}}</style></head>
            <body><div id="root"></div><script>{{js}}</script></body></html>
            """;
    }

    private static string ReadEmbeddedResource(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            // Try to find the resource by suffix in case the logical name differs.
            foreach (var n in assembly.GetManifestResourceNames())
            {
                if (n.EndsWith(name, StringComparison.OrdinalIgnoreCase) ||
                    n.EndsWith(name[name.LastIndexOf('.')..], StringComparison.OrdinalIgnoreCase))
                {
                    name = n;
                    break;
                }
            }

            stream = assembly.GetManifestResourceStream(name);
        }

        if (stream is null)
        {
            return string.Empty;
        }

        using (stream)
        {
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
