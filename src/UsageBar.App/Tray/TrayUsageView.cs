using UsageBar.Application;
using UsageBar.Domain;
using UsageBar.Tooltip;

namespace UsageBar.Tray;

/// <summary>
/// Adapts the platform-agnostic <see cref="IUsageView"/> contract onto the Win32 tray icon
/// and WebView2 tooltip. All members are safe to call from the background refresh thread
/// (the icon and balloon use thread-safe shell calls; the tooltip marshals via a posted message).
/// </summary>
internal sealed class TrayUsageView(TrayIconWindow window, WebViewTooltip tooltip) : IUsageView
{
    public void ShowIcon(IReadOnlyList<IconLayout.Bar> bars) => window.UpdateIcon(bars);

    public void ShowCards(IReadOnlyList<TooltipCard> cards) => tooltip.SetContent(cards);

    public void Notify(NotificationLevel level, string message) => window.ShowBalloon(level, message);
}
