using AppKit;
using UsageBar.Core.Domain;

namespace UsageBar.MacOS.Tray;

internal interface INativeTray
{
    event Action? SettingsRequested;
    event Action? RefreshRequested;
    event Action? ExitRequested;

    void UpdateIcon(NSImage image);
    void ShowNotification(NotificationLevel level, string message);
}