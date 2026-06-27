using System.Runtime.InteropServices;

namespace UsageBar.Tray;

internal static class NativeMethods
{
    public const int CallbackMessage = 0x8000 + 1;
    public const int WmDestroy = 0x0002;
    public const int WmContextMenu = 0x007B;
    public const int WmRButtonUp = 0x0205;
    public const int WmNull = 0x0000;

    // ── Window styles ───────────────────────────────────────
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_NOACTIVATE = 0x08000000;

    // ── SetWindowPos ────────────────────────────────────────
    public static readonly nint HWND_TOPMOST = -1;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;

    // ── ShowWindow ──────────────────────────────────────────
    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;

    // ── SystemParametersInfo ────────────────────────────────
    public const uint SPI_GETWORKAREA = 0x0030;

    // ── Tray version 4 + popup events (requires NOTIFYICON_VERSION_4) ─
    public const uint NimSetVersion = 0x00000004;
    public const uint NotifyIconVersion4 = 4;
    public const uint NinPopupOpen = 0x0406;
    public const uint NinPopupClose = 0x0407;

    // ── Custom messages ─────────────────────────────────────
    public const uint WM_APP = 0x8000;
    public const uint WmTooltipSetData = WM_APP + 3;   // re-render trigger
    public const uint WmRunDelegate = WM_APP + 4;      // pump SynchronizationContext

    // ── Menu flags ──────────────────────────────────────────
    public const uint MfString = 0x00000000;
    public const uint MfChecked = 0x00000008;
    public const uint MfPopup = 0x00000010;
    public const uint MfSeparator = 0x00000800;
    public const uint TpmRightButton = 0x0002;
    public const uint TpmReturnCmd = 0x0100;

    public const uint NifMessage = 0x00000001;
    public const uint NifIcon = 0x00000002;
    public const uint NifInfo = 0x00000010;
    public const uint NiifInfo = 0x00000001;
    public const uint NiifWarning = 0x00000002;
    public const uint NiifError = 0x00000003;

    public const uint NimAdd = 0x00000000;
    public const uint NimModify = 0x00000001;
    public const uint NimDelete = 0x00000002;

    public delegate nint WndProc(nint hWnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NotifyIconData
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NotifyIconIdentifier
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public Point pt;
        public uint lPrivate;
    }

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern sbyte GetMessage(out Msg lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    public static extern nint DispatchMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(
        nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref Rect pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool AppendMenu(nint hMenu, uint uFlags, nuint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hWnd, nint lptpm);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint CreateIcon(nint hInstance, int nWidth, int nHeight, byte cPlanes, byte cBitsPixel, byte[] lpbANDbits, byte[] lpbXORbits);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(nint hIcon);

    [StructLayout(LayoutKind.Sequential)]
    public struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(nint hWnd, ref Margins pMarInset);

    [DllImport("gdi32.dll")]
    public static extern nint CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

    [DllImport("user32.dll")]
    public static extern int SetWindowRgn(nint hWnd, nint hRgn, bool bRedraw);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool ShellNotifyIcon(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect")]
    public static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out Rect iconLocation);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    public static extern nint GetCurrentProcess();

    [DllImport("psapi.dll", SetLastError = true)]
    public static extern bool EmptyWorkingSet(nint hProcess);

    /// <summary>
    /// Returns the process working set to the OS; the pages fault back in lazily on next touch.
    /// A cheap way to keep the idle footprint low for this resident tray app — call it after
    /// bursts of allocation (a refresh) or after freeing memory (hiding/suspending the tooltip).
    /// </summary>
    public static void TrimWorkingSet() => EmptyWorkingSet(GetCurrentProcess());

    /// <summary>
    /// Registers a process-unique window class (prefix + GUID) for the given window procedure.
    /// Returns the generated class name, or <see langword="null"/> when registration fails.
    /// </summary>
    public static string? RegisterWindowClass(string namePrefix, WndProc wndProc, nint instance)
    {
        var className = $"{namePrefix}-{Guid.NewGuid():N}";
        var windowClass = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = wndProc,
            hInstance = instance,
            lpszClassName = className,
        };

        return RegisterClassEx(ref windowClass) == 0 ? null : className;
    }
}
