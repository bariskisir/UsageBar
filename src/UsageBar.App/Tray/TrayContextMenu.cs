using UsageBar.Application;
using UsageBar.Configuration;

namespace UsageBar.Tray;

/// <summary>
/// Builds and tracks the tray right-click menu. Reads/writes settings through
/// <see cref="ISettingsStore"/> and raises <see cref="RefreshRequested"/> /
/// <see cref="ExitRequested"/>; it holds no UI state of its own.
/// </summary>
internal sealed class TrayContextMenu(ISettingsStore settings) : ITrayContextMenu
{
    private const uint TestNotificationCommandId = 1000;
    private const uint RefreshCommandId = 1001;
    private const uint ExitCommandId = 1002;
    private const uint RefreshEveryBase = 2001;
    private const uint HighLevelBase = 3001;
    private const uint CriticalLevelBase = 4001;

    private static readonly int[] RefreshEveryValues = [1, 5, 15, 60];
    private static readonly int[] LevelValues = [50, 60, 70, 75, 80, 85, 90, 95];

    /// <summary>Raised when an immediate refresh is requested (Refresh item or a settings change).</summary>
    public event Action? RefreshRequested;

    /// <summary>Raised when the user chooses Test Notification.</summary>
    public event Action? TestNotificationRequested;

    /// <summary>Raised when the user chooses Exit.</summary>
    public event Action? ExitRequested;

    public void Show(nint ownerHwnd, NativeMethods.Point point)
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            var current = settings.Read();

            var refreshEvery = BuildCheckedSubmenu(RefreshEveryBase, RefreshEveryValues, current.RefreshPeriodMinute, static value => $"{value} min");
            NativeMethods.AppendMenu(menu, NativeMethods.MfPopup, (nuint)refreshEvery, "Refresh every");

            var highLevel = BuildCheckedSubmenu(HighLevelBase, LevelValues, (int)current.HighPercentage, static value => $"{value}%");
            NativeMethods.AppendMenu(menu, NativeMethods.MfPopup, (nuint)highLevel, "High Level");

            var criticalLevel = BuildCheckedSubmenu(CriticalLevelBase, LevelValues, (int)current.CriticalPercentage, static value => $"{value}%");
            NativeMethods.AppendMenu(menu, NativeMethods.MfPopup, (nuint)criticalLevel, "Critical Level");

            NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, 0, string.Empty);
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, TestNotificationCommandId, "Test Notification");
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, RefreshCommandId, "Refresh");
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, ExitCommandId, "Exit");

            NativeMethods.SetForegroundWindow(ownerHwnd);

            var command = NativeMethods.TrackPopupMenuEx(
                menu,
                NativeMethods.TpmRightButton | NativeMethods.TpmReturnCmd,
                point.X,
                point.Y,
                ownerHwnd,
                0);

            if (command != 0)
            {
                HandleCommand(command, current);
            }

            // Flush the menu message so the next click is delivered reliably.
            NativeMethods.PostMessage(ownerHwnd, NativeMethods.WmNull, 0, 0);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private void HandleCommand(uint commandId, AppSettings current)
    {
        switch (commandId)
        {
            case TestNotificationCommandId:
                TestNotificationRequested?.Invoke();
                break;

            case RefreshCommandId:
                RefreshRequested?.Invoke();
                break;

            case ExitCommandId:
                ExitRequested?.Invoke();
                break;

            case >= RefreshEveryBase and < RefreshEveryBase + 4 when commandId - RefreshEveryBase < RefreshEveryValues.Length:
                ApplySettings(current with { RefreshPeriodMinute = RefreshEveryValues[(int)(commandId - RefreshEveryBase)] });
                break;

            case >= HighLevelBase and < HighLevelBase + 8 when commandId - HighLevelBase < LevelValues.Length:
                ApplySettings(current with { HighPercentage = LevelValues[(int)(commandId - HighLevelBase)] });
                break;

            case >= CriticalLevelBase and < CriticalLevelBase + 8 when commandId - CriticalLevelBase < LevelValues.Length:
                ApplySettings(current with { CriticalPercentage = LevelValues[(int)(commandId - CriticalLevelBase)] });
                break;
        }
    }

    private void ApplySettings(AppSettings updated)
    {
        settings.Write(updated);
        RefreshRequested?.Invoke();
    }

    private static nint BuildCheckedSubmenu(uint baseId, int[] values, int selected, Func<int, string> label)
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            return 0;
        }

        for (var i = 0; i < values.Length; i++)
        {
            var flags = values[i] == selected
                ? NativeMethods.MfString | NativeMethods.MfChecked
                : NativeMethods.MfString;
            NativeMethods.AppendMenu(menu, flags, (nuint)(baseId + (uint)i), label(values[i]));
        }

        return menu;
    }
}
