using UsageBar.Core.Application;
using UsageBar.Core.Domain;
using UsageBar.Linux.Tooltip;
using UsageBar.Linux.Tray;

namespace UsageBar.Linux;

internal sealed class UsageView(
    NativeTray tray,
    FallbackStatusWindow fallbackStatusWindow,
    NativeTooltip tooltip) : IUsageView
{
    public void ShowIcon(IReadOnlyList<IconLayout.Bar> bars)
    {
        tray.UpdateIcon(bars);
        fallbackStatusWindow.UpdateIcon(bars);
    }

    public void ShowCards(IReadOnlyList<TooltipCard> cards, int scale)
    {
        tooltip.SetContent(cards, scale);
    }

    public void Notify(NotificationLevel level, string message)
    {
        tray.ShowNotification(level, message);
    }
}
