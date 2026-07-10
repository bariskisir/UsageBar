using UsageBar.Core.Application;
using UsageBar.Core.Domain;
using UsageBar.Linux.Tooltip;
using UsageBar.Linux.Tray;

namespace UsageBar.Linux;

internal sealed class UsageView(NativeTray tray, NativeTooltip tooltip) : IUsageView
{
    public void ShowIcon(IReadOnlyList<IconLayout.Bar> bars)
    {
        tray.UpdateIcon(bars);
    }

    public void ShowCards(IReadOnlyList<TooltipCard> cards)
    {
        tooltip.SetContent(cards);
    }

    public void Notify(NotificationLevel level, string message)
    {
        tray.ShowNotification(level, message);
    }
}
