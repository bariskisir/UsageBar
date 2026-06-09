using System.Reflection;
using System.Runtime.InteropServices;
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
internal sealed class WebViewTooltip : IDisposable
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
            _env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: ApplicationPaths.WebView2DataDirectory,
                options: null).ConfigureAwait(true);

            _controller = await _env.CreateCoreWebView2ControllerAsync(_hwnd).ConfigureAwait(true);
            _core = _controller.CoreWebView2;
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, initialWidth, initialHeight);
            _controller.IsVisible = true;

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
                if (_hwnd != 0)
                {
                    NativeMethods.PostMessage(_hwnd, NativeMethods.WmTooltipSetData, 0, 0);
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

        var scale = WindowScale();
        var heightCss = Math.Max(_heightCss, MinHeight);
        var width = Scaled(Width, scale);
        var height = Scaled(heightCss, scale);
        var gap = Scaled(8, scale);
        var radius = Scaled(CornerRadius, scale);

        var icon = _lastIconRect;
        var work = WorkArea();

        // Anchor above the tray icon, flipping below when it would clip the top.
        var x = icon.Right - width;
        var y = icon.Top - height - gap;
        if (y < work.Top)
        {
            y = icon.Bottom + gap;
        }

        if (x + width > work.Right)
        {
            x = work.Right - width - 4;
        }

        if (x < work.Left)
        {
            x = work.Left + 4;
        }

        if (y + height > work.Bottom)
        {
            y = work.Bottom - height - 4;
        }

        if (y < work.Top)
        {
            y = work.Top + 4;
        }

        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, x, y, width, height, NativeMethods.SWP_NOACTIVATE);

        if (_controller is not null)
        {
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, width, height);
        }

        var region = NativeMethods.CreateRoundRectRgn(0, 0, width + 1, height + 1, radius, radius);
        NativeMethods.SetWindowRgn(_hwnd, region, true);
    }

    private nint CreatePopupWindow(nint instance)
    {
        var className = $"UsageBarWebTooltip-{Guid.NewGuid():N}";

        var windowClass = new NativeMethods.WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WndClassEx>(),
            lpfnWndProc = _wndProc,
            hInstance = instance,
            lpszClassName = className,
        };

        if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
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
        return reader.ReadToEnd();
    }
}
