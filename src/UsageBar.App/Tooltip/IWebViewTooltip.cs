using UsageBar.Domain;
using UsageBar.Tray;

namespace UsageBar.Tooltip;

internal interface IWebViewTooltip
{
    nint Hwnd { get; }

    Task<bool> InitAsync(nint instance);
    void SetContent(IReadOnlyList<TooltipCard> cards);
    void ShowNearIcon(NativeMethods.Rect? iconRect, int fallbackX, int fallbackY);
    void Hide();
}
