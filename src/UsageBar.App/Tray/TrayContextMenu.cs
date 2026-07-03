namespace UsageBar.Tray;

internal sealed class TrayContextMenu : ITrayContextMenu
{
    private const uint SettingsCommandId = 1003;
    private const uint RefreshCommandId = 1001;
    private const uint ExitCommandId = 1002;

    public event Action? RefreshRequested;
    public event Action? ExitRequested;
    public event Action? SettingsRequested;

    public void Show(nint ownerHwnd, NativeMethods.Point point)
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, SettingsCommandId, "Settings");
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
                HandleCommand(command);
            }

            NativeMethods.PostMessage(ownerHwnd, NativeMethods.WmNull, 0, 0);
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
            case SettingsCommandId:
                SettingsRequested?.Invoke();
                break;

            case RefreshCommandId:
                RefreshRequested?.Invoke();
                break;

            case ExitCommandId:
                ExitRequested?.Invoke();
                break;
        }
    }
}
