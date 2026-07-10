namespace UsageBar.Windows.Tray;

internal interface ITrayContextMenu
{
    event Action? RefreshRequested;
    event Action? ExitRequested;
    event Action? SettingsRequested;

    void Show(nint ownerHwnd, NativeMethods.Point point);
}
