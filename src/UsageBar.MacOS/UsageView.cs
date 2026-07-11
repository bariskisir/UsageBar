using UsageBar.Core.Application;
using UsageBar.Core.Domain;
using UsageBar.MacOS.Tooltip;
using UsageBar.MacOS.Tray;

namespace UsageBar.MacOS;

internal sealed class UsageView(NativeTray tray, NativeTooltip tooltip) : IUsageView
{
    public void ShowIcon(IReadOnlyList<IconLayout.Bar> bars)
    {
        var image = NativeTray.RenderIcon(bars);
        tray.UpdateIcon(image);
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