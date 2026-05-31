using System.Runtime.InteropServices;

namespace UsageBar.Shell.Tray;

internal sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;
    private const uint RefreshCommandId = 1001;
    private const uint ExitCommandId = 1002;
    private readonly NativeMethods.WndProc _wndProc;
    private readonly nint _instance;
    private readonly nint _windowHandle;
    private readonly Lock _iconGate = new();
    private string _tooltipText = "UsageBar\nLoading...";
    private nint _iconHandle;
    private bool _disposed;

    public TrayIcon()
    {
        _wndProc = WindowProc;
        _instance = NativeMethods.GetModuleHandle(null);
        var className = $"UsageBarTrayWindow-{Guid.NewGuid():N}";

        var windowClass = new NativeMethods.WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WndClassEx>(),
            lpfnWndProc = _wndProc,
            hInstance = _instance,
            lpszClassName = className
        };

        if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
        {
            throw new InvalidOperationException("Failed to register tray window class.");
        }

        _windowHandle = NativeMethods.CreateWindowEx(
            0,
            className,
            "UsageBar",
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            _instance,
            0);

        if (_windowHandle == 0)
        {
            throw new InvalidOperationException("Failed to create tray window.");
        }

        _iconHandle = IconFactory.CreateUsageIcon(null, null);
        AddIcon(_tooltipText);
    }

    public event EventHandler? RefreshRequested;

    public event EventHandler? ExitRequested;

    public void RunMessageLoop()
    {
        while (NativeMethods.GetMessage(out var message, 0, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }
    }

    public void UpdateTooltip(string tooltip)
    {
        lock (_iconGate)
        {
            var data = CreateNotifyIconData();
            data.uFlags = NativeMethods.NifTip | NativeMethods.NifShowTip;
            data.szTip = LimitTooltip(tooltip);
            NativeMethods.ShellNotifyIcon(NativeMethods.NimModify, ref data);
            _tooltipText = tooltip;
        }
    }

    public void UpdateIcon(double? codexPrimaryUsedPercent, double? codexSecondaryUsedPercent)
    {
        var nextIcon = IconFactory.CreateUsageIcon(codexPrimaryUsedPercent, codexSecondaryUsedPercent);
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

    public void ShowNotification(string title, string message)
    {
        lock (_iconGate)
        {
            var data = CreateNotifyIconData();
            data.uFlags = NativeMethods.NifInfo;
            data.szInfoTitle = LimitBalloonTitle(title);
            data.szInfo = LimitBalloonText(message);
            data.dwInfoFlags = NativeMethods.NiifInfo;
            NativeMethods.ShellNotifyIcon(NativeMethods.NimModify, ref data);
        }
    }

    private nint WindowProc(nint hWnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case NativeMethods.CallbackMessage:
                HandleTrayCallback(lParam);
                return 0;

            case NativeMethods.WmDestroy:
                NativeMethods.PostQuitMessage(0);
                return 0;
        }

        return NativeMethods.DefWindowProc(hWnd, message, wParam, lParam);
    }

    private void HandleTrayCallback(nint lParam)
    {
        var mouseMessage = (uint)(lParam.ToInt64() & 0xffff);
        if (mouseMessage is NativeMethods.WmRButtonUp or NativeMethods.WmContextMenu)
        {
            ShowContextMenu();
        }
    }

    private void ShowContextMenu()
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            return;
        }

        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, RefreshCommandId, "Refresh");
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, ExitCommandId, "Exit");
            NativeMethods.SetForegroundWindow(_windowHandle);

            var command = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TpmRightButton | NativeMethods.TpmReturnCmd,
                point.X,
                point.Y,
                _windowHandle,
                0);

            if (command != 0)
            {
                HandleCommand(command);
            }

            NativeMethods.PostMessage(_windowHandle, NativeMethods.WmNull, 0, 0);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private void HandleCommand(uint commandId)
    {
        switch (commandId)
        {
            case RefreshCommandId:
                RefreshRequested?.Invoke(this, EventArgs.Empty);
                break;

            case ExitCommandId:
                ExitRequested?.Invoke(this, EventArgs.Empty);
                Dispose();
                NativeMethods.PostQuitMessage(0);
                break;
        }
    }

    private void AddIcon(string tooltip)
    {
        var data = CreateNotifyIconData();
        data.uFlags = NativeMethods.NifMessage | NativeMethods.NifIcon | NativeMethods.NifTip | NativeMethods.NifShowTip;
        data.uCallbackMessage = NativeMethods.CallbackMessage;
        data.hIcon = _iconHandle;
        data.szTip = LimitTooltip(tooltip);

        if (!NativeMethods.ShellNotifyIcon(NativeMethods.NimAdd, ref data))
        {
            throw new InvalidOperationException("Failed to add tray icon.");
        }
    }

    private NativeMethods.NotifyIconData CreateNotifyIconData()
    {
        return new NativeMethods.NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(),
            hWnd = _windowHandle,
            uID = IconId,
            szTip = string.Empty,
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };
    }

    private static string LimitTooltip(string tooltip)
    {
        return tooltip.Length <= 127 ? tooltip : tooltip[..127];
    }

    private static string LimitBalloonTitle(string title)
    {
        return title.Length <= 63 ? title : title[..63];
    }

    private static string LimitBalloonText(string text)
    {
        return text.Length <= 255 ? text : text[..255];
    }

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
