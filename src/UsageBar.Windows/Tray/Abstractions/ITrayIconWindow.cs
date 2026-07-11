using UsageBar.Core.Application;
using UsageBar.Core.Domain;

namespace UsageBar.Windows.Tray;

internal interface ITrayIconWindow
{
    nint Hwnd { get; }

    event Action<NativeMethods.Rect?, int, int>? TooltipShowRequested;
    event Action? TooltipHideRequested;

    void RunMessageLoop();
    void Quit();
    void UpdateIcon(IReadOnlyList<IconLayout.Bar> bars);
    void ShowBalloon(NotificationLevel level, string message);
    void ShowBalloon(NotificationLevel level, string message, Action? onClick);
}