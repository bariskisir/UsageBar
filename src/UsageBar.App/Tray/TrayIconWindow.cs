using System.Runtime.InteropServices;
using UsageBar.Application;
using UsageBar.Domain;

namespace UsageBar.Tray;

/// <summary>
/// The hidden Win32 message-loop window that owns the notification-area icon. Renders the
/// usage icon, shows balloon notifications, displays the context menu, and raises tooltip
/// show/hide events from the version-4 popup messages. All tray data is pushed in; this
/// type holds no settings or business state.
/// </summary>
internal sealed class TrayIconWindow : ITrayIconWindow, IDisposable
{
    private const uint IconId = 1;
    private const string IconName = "Usage Bar";

    private readonly ITrayContextMenu _contextMenu;
    private readonly NativeMethods.WndProc _wndProc;
    private readonly nint _instance;
    private readonly nint _windowHandle;
    private readonly Lock _iconGate = new();
    private nint _iconHandle;
    private bool _disposed;

    public TrayIconWindow(ITrayContextMenu contextMenu)
    {
        _contextMenu = contextMenu;
        _wndProc = WindowProc;
        _instance = NativeMethods.GetModuleHandle(null);

        var className = NativeMethods.RegisterWindowClass("UsageBarTrayWindow", _wndProc, _instance)
            ?? throw new InvalidOperationException("Failed to register tray window class.");

        _windowHandle = NativeMethods.CreateWindowEx(0, className, "UsageBar", 0, 0, 0, 0, 0, 0, 0, _instance, 0);
        if (_windowHandle == 0)
        {
            throw new InvalidOperationException("Failed to create tray window.");
        }

        _iconHandle = IconRenderer.CreateUsageIcon([]);
        AddIcon();
    }

    /// <summary>The hidden tray message-loop window handle.</summary>
    public nint Hwnd => _windowHandle;

    /// <summary>Raised on hover (NIN_POPUPOPEN) with the icon rectangle and a cursor fallback.</summary>
    public event Action<NativeMethods.Rect?, int, int>? TooltipShowRequested;

    /// <summary>Raised when the hover popup closes (NIN_POPUPCLOSE).</summary>
    public event Action? TooltipHideRequested;

    public void RunMessageLoop()
    {
        while (NativeMethods.GetMessage(out var message, 0, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }
    }

    /// <summary>Stops the message loop. Safe to call from the UI thread.</summary>
    public void Quit() => NativeMethods.PostQuitMessage(0);

    public void UpdateIcon(IReadOnlyList<IconLayout.Bar> bars)
    {
        var nextIcon = IconRenderer.CreateUsageIcon(bars);
        nint previousIcon;

        lock (_iconGate)
        {
            previousIcon = _iconHandle;
            _iconHandle = nextIcon;

            var data = CreateNotifyIconData();
            data.uFlags = NativeMethods.NifIcon;
            data.hIcon = _iconHandle;
            NativeMethods.ShellNotifyIcon(NativeMethods.NimModify, ref data);
        }

        if (previousIcon != 0)
        {
            NativeMethods.DestroyIcon(previousIcon);
        }
    }

    public void ShowBalloon(NotificationLevel level, string message)
    {
        var infoFlag = level switch
        {
            NotificationLevel.Reset => NativeMethods.NiifInfo,
            NotificationLevel.High => NativeMethods.NiifWarning,
            NotificationLevel.Critical => NativeMethods.NiifError,
            _ => NativeMethods.NiifInfo,
        };

        lock (_iconGate)
        {
            var data = CreateNotifyIconData();
            data.uFlags = NativeMethods.NifInfo;
            data.szInfoTitle = "";
            data.szInfo = Truncate(message, 255);
            data.dwInfoFlags = infoFlag;
            NativeMethods.ShellNotifyIcon(NativeMethods.NimModify, ref data);
        }
    }

    private nint WindowProc(nint hWnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case NativeMethods.CallbackMessage:
                HandleTrayCallback(lParam, wParam);
                return 0;

            case NativeMethods.WmRunDelegate:
                TrayUiSyncContext.DrainCurrent();
                return 0;

            case NativeMethods.WmDestroy:
                NativeMethods.PostQuitMessage(0);
                return 0;
        }

        return NativeMethods.DefWindowProc(hWnd, message, wParam, lParam);
    }

    private void HandleTrayCallback(nint lParam, nint wParam)
    {
        var eventId = (uint)(lParam.ToInt64() & 0xffff);

        switch (eventId)
        {
            case NativeMethods.WmRButtonUp:
            case NativeMethods.WmContextMenu:
                if (NativeMethods.GetCursorPos(out var point))
                {
                    _contextMenu.Show(_windowHandle, point);
                }

                break;

            case NativeMethods.NinPopupOpen:
                var (x, y) = HoverAnchor(wParam);
                TooltipShowRequested?.Invoke(TryGetIconRect(), x, y);
                break;

            case NativeMethods.NinPopupClose:
                TooltipHideRequested?.Invoke();
                break;
        }
    }

    private static (int X, int Y) HoverAnchor(nint wParam)
    {
        var raw = wParam.ToInt64();
        var x = (short)(raw & 0xffff);
        var y = (short)((raw >> 16) & 0xffff);
        if (x != 0 || y != 0)
        {
            return (x, y);
        }

        return NativeMethods.GetCursorPos(out var point) ? (point.X, point.Y) : (0, 0);
    }

    private NativeMethods.Rect? TryGetIconRect()
    {
        var identifier = new NativeMethods.NotifyIconIdentifier
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NotifyIconIdentifier>(),
            hWnd = _windowHandle,
            uID = IconId,
            guidItem = Guid.Empty,
        };

        var hr = NativeMethods.Shell_NotifyIconGetRect(ref identifier, out var rect);
        if (hr == 0 && rect.Right > rect.Left && rect.Bottom > rect.Top)
        {
            return rect;
        }

        return null;
    }

    private void AddIcon()
    {
        var data = CreateNotifyIconData();
        data.uFlags = NativeMethods.NifMessage | NativeMethods.NifIcon;
        data.uCallbackMessage = NativeMethods.CallbackMessage;
        data.hIcon = _iconHandle;

        if (!NativeMethods.ShellNotifyIcon(NativeMethods.NimAdd, ref data))
        {
            throw new InvalidOperationException("Failed to add tray icon.");
        }

        // Request version 4 so the shell sends NIN_POPUPOPEN / NIN_POPUPCLOSE. We register no
        // tip text (no NIF_TIP) so the shell never draws its own tooltip — only our WebView2
        // popup shows on hover, with no stray gray box peeking out from behind it.
        data.uFlags = 0;
        data.uTimeoutOrVersion = NativeMethods.NotifyIconVersion4;
        NativeMethods.ShellNotifyIcon(NativeMethods.NimSetVersion, ref data);
    }

    private NativeMethods.NotifyIconData CreateNotifyIconData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(),
        hWnd = _windowHandle,
        uID = IconId,
        szTip = string.Empty,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var data = CreateNotifyIconData();
        NativeMethods.ShellNotifyIcon(NativeMethods.NimDelete, ref data);

        if (_iconHandle != 0)
        {
            NativeMethods.DestroyIcon(_iconHandle);
            _iconHandle = 0;
        }

        if (_windowHandle != 0)
        {
            NativeMethods.DestroyWindow(_windowHandle);
        }
    }
}
