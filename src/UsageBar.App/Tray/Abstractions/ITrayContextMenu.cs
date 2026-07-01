namespace UsageBar.Tray;

internal interface ITrayContextMenu
{
    event Action? RefreshRequested;
    event Action? TestNotificationRequested;
    event Action? ExitRequested;
    event Action? UpdateCheckNowRequested;

    void Show(nint ownerHwnd, NativeMethods.Point point);
}
