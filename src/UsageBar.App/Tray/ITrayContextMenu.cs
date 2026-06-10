namespace UsageBar.Tray;

internal interface ITrayContextMenu
{
    event Action? RefreshRequested;
    event Action? ExitRequested;

    void Show(nint ownerHwnd, NativeMethods.Point point);
}
