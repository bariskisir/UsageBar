using UsageBar.Core.Domain;
using UsageBar.Windows.Tray;

namespace UsageBar.Windows.Tooltip;

internal interface IWebViewTooltip
{
    nint Hwnd { get; }

    Task<bool> InitAsync(nint instance);
    void SetContent(IReadOnlyList<TooltipCard> cards);
    void ShowNearIcon(NativeMethods.Rect? iconRect, int fallbackX, int fallbackY);
    void Hide();
}
