using System.Runtime.InteropServices;
using UsageBar.Domain;
using UsageBar.Infrastructure.Configuration;

namespace UsageBar.Shell.Tray;

internal sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;

    // ── Command IDs ──────────────────────────────────────
    private const uint RefreshCommandId = 1001;
    private const uint ExitCommandId = 1002;

    private const uint RefreshEveryBase = 2001;
    private static readonly int[] RefreshEveryValues = [1, 5, 15, 60];

    private const uint HighLevelBase = 3001;
    private const uint CriticalLevelBase = 4001;
    private static readonly int[] LevelValues = [10, 20, 30, 40, 50, 60, 70, 80, 90];

    private readonly NativeMethods.WndProc _wndProc;
    private readonly nint _instance;
    private readonly nint _windowHandle;
    private readonly Lock _iconGate = new();
    private readonly WebViewTooltip? _tooltip;
    private readonly bool _useLegacyTooltip;
    private string _tooltipText = "UsageBar\nLoading...";
    private nint _iconHandle;
    private bool _disposed;

    /// <summary>The hidden tray message-loop window handle.</summary>
    public nint Hwnd => _windowHandle;

    /// <summary>
    /// Static reference so the window proc (UI thread) can read/write settings
    /// without async.
    /// </summary>
    private static SettingsService? s_settingsService;

    public TrayIcon(WebViewTooltip? tooltip = null, SettingsService? settingsService = null)
    {
        s_settingsService = settingsService;
        _tooltip = tooltip;
        _useLegacyTooltip = tooltip is null || tooltip.UseLegacyTooltip;

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

        _iconHandle = IconFactory.CreateUsageIcon([], []);
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
        if (!_useLegacyTooltip)
        {
            _tooltipText = tooltip;
            return;
        }

        lock (_iconGate)
        {
            var data = CreateNotifyIconData();
            data.uFlags = NativeMethods.NifTip | NativeMethods.NifShowTip;
            data.szTip = LimitTooltip(tooltip);
            NativeMethods.ShellNotifyIcon(NativeMethods.NimModify, ref data);
            _tooltipText = tooltip;
        }
    }

    /// <summary>
    /// Pushes tooltip cards to the WebView2 popup. No-op in legacy mode.
    /// </summary>
    public void UpdateCards(IReadOnlyList<TooltipCard> cards)
    {
        if (_useLegacyTooltip || _tooltip is null)
        {
            return;
        }

        _tooltip.SetContent(cards);
    }

    public void UpdateIcon(IReadOnlyList<UsageBarWindow> windows, IReadOnlyList<(string Provider, string Plan)> plans)
    {
        var nextIcon = IconFactory.CreateUsageIcon(windows, plans);
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

    public void ShowNotification(NotificationLevel level, string message)
    {
        // Pick one of Windows' built-in balloon glyphs: reset → info,
        // high → warning, critical → error.
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
            data.szInfoTitle = "Usage Bar";
            data.szInfo = LimitBalloonText(message);
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
                if (SynchronizationContext.Current is TrayUiSyncContext ctx)
                {
                    ctx.Drain();
                }
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
                ShowContextMenu();
                break;

            case NativeMethods.NinPopupOpen:
                if (_tooltip is not null)
                {
                    var rect = TryGetIconRect();
                    var (x, y) = HoverAnchor(wParam);
                    _tooltip.ShowNearIcon(rect, x, y);
                }
                break;

            case NativeMethods.NinPopupClose:
                _tooltip?.Hide();
                break;
        }
    }

    private static (int x, int y) HoverAnchor(nint wParam)
    {
        var raw = wParam.ToInt64();
        var x = (short)(raw & 0xffff);
        var y = (short)((raw >> 16) & 0xffff);
        if (x != 0 || y != 0)
        {
            return (x, y);
        }

        if (NativeMethods.GetCursorPos(out var point))
        {
            return (point.X, point.Y);
        }

        return (0, 0);
    }

    private NativeMethods.Rect? TryGetIconRect()
    {
        var identifier = new NativeMethods.NotifyIconIdentifier
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NotifyIconIdentifier>(),
            hWnd = _windowHandle,
            uID = IconId,
            guidItem = Guid.Empty
        };

        var hr = NativeMethods.Shell_NotifyIconGetRect(ref identifier, out var rect);
        if (hr == 0 && rect.Right > rect.Left && rect.Bottom > rect.Top)
        {
            return rect;
        }

        return null;
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
            var settings = ReadCurrentSettings();

            // ── Refresh every submenu ──
            var refreshEveryMenu = SubmenuWithCheckedInt(
                RefreshEveryBase,
                RefreshEveryValues,
                settings.RefreshPeriodMinute,
                v => $"{v} min");
            NativeMethods.AppendMenu(menu, NativeMethods.MfPopup, (nuint)refreshEveryMenu, "Refresh every");

            // ── High Level submenu ──
            var highMenu = SubmenuWithCheckedInt(
                HighLevelBase,
                LevelValues,
                (int)settings.HighPercentage,
                v => $"{v}%");
            NativeMethods.AppendMenu(menu, NativeMethods.MfPopup, (nuint)highMenu, "High Level");

            // ── Critical Level submenu ──
            var criticalMenu = SubmenuWithCheckedInt(
                CriticalLevelBase,
                LevelValues,
                (int)settings.CriticalPercentage,
                v => $"{v}%");
            NativeMethods.AppendMenu(menu, NativeMethods.MfPopup, (nuint)criticalMenu, "Critical Level");

            // ── Separator ──
            NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, 0, null!);

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

            // ── Refresh period ──
            case >= RefreshEveryBase and < RefreshEveryBase + 4:
            {
                var idx = (int)(commandId - RefreshEveryBase);
                var s = ReadCurrentSettings() with { RefreshPeriodMinute = RefreshEveryValues[idx] };
                ApplySettings(s);
                break;
            }

            // ── High level threshold ──
            case >= HighLevelBase and < HighLevelBase + 9:
            {
                var idx = (int)(commandId - HighLevelBase);
                var s = ReadCurrentSettings() with { HighPercentage = LevelValues[idx] };
                ApplySettings(s);
                break;
            }

            // ── Critical level threshold ──
            case >= CriticalLevelBase and < CriticalLevelBase + 9:
            {
                var idx = (int)(commandId - CriticalLevelBase);
                var s = ReadCurrentSettings() with { CriticalPercentage = LevelValues[idx] };
                ApplySettings(s);
                break;
            }
        }
    }

    private static AppSettings ReadCurrentSettings()
    {
        if (s_settingsService is not null)
        {
            return s_settingsService.ReadSync();
        }

        return AppSettings.Default;
    }

    private void ApplySettings(AppSettings settings)
    {
        s_settingsService?.WriteSync(settings);
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private static nint SubmenuWithCheckedInt(uint baseId, int[] values, int selected, Func<int, string> labelFor)
    {
        var hMenu = NativeMethods.CreatePopupMenu();
        if (hMenu == 0)
        {
            return 0;
        }

        for (var i = 0; i < values.Length; i++)
        {
            var id = (nuint)(baseId + (uint)i);
            var flags = values[i] == selected
                ? NativeMethods.MfString | NativeMethods.MfChecked
                : NativeMethods.MfString;
            NativeMethods.AppendMenu(hMenu, flags, id, labelFor(values[i]));
        }

        return hMenu;
    }

    private void AddIcon(string tooltip)
    {
        var data = CreateNotifyIconData();
        data.uFlags = NativeMethods.NifMessage | NativeMethods.NifIcon | NativeMethods.NifTip;
        if (_useLegacyTooltip)
        {
            data.uFlags |= NativeMethods.NifShowTip;
        }

        data.uCallbackMessage = NativeMethods.CallbackMessage;
        data.hIcon = _iconHandle;
        data.szTip = LimitTooltip(tooltip);

        if (!NativeMethods.ShellNotifyIcon(NativeMethods.NimAdd, ref data))
        {
            throw new InvalidOperationException("Failed to add tray icon.");
        }

        // Request version 4 so the shell sends NIN_POPUPOPEN / NIN_POPUPCLOSE.
        if (!_useLegacyTooltip)
        {
            data.uFlags = 0;
            data.uTimeoutOrVersion = NativeMethods.NotifyIconVersion4;
            NativeMethods.ShellNotifyIcon(NativeMethods.NimSetVersion, ref data);
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
