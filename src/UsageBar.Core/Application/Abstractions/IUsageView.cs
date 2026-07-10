using UsageBar.Core.Domain;

namespace UsageBar.Core.Application;

/// <summary>
/// The tray presentation surface the refresh pipeline drives. Implemented by the Windows
/// shell (tray icon + WebView2 tooltip + balloon notifications). Keeping this an interface
/// decouples the refresh/aggregation logic from Win32 so it stays platform-agnostic and testable.
/// </summary>
public interface IUsageView
{
    /// <summary>Updates the tray icon from the latest laid-out bars (already ordered and weighted).</summary>
    void ShowIcon(IReadOnlyList<IconLayout.Bar> bars);

    /// <summary>Pushes the latest tooltip cards to the popup.</summary>
    void ShowCards(IReadOnlyList<TooltipCard> cards);

    /// <summary>Shows a balloon notification at the given severity.</summary>
    void Notify(NotificationLevel level, string message);
}
