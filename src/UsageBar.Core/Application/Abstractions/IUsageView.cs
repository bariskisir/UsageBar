using UsageBar.Domain;

namespace UsageBar.Application;

/// <summary>
/// The tray presentation surface the refresh pipeline drives. Implemented by the Windows
/// shell (tray icon + WebView2 tooltip + balloon notifications). Keeping this an interface
/// decouples the refresh/aggregation logic from Win32 so it stays platform-agnostic and testable.
/// </summary>
public interface IUsageView
{
    /// <summary>Updates the tray icon bars from the latest usage windows and plans.</summary>
    void ShowIcon(IReadOnlyList<UsageWindow> windows, IReadOnlyList<ProviderPlan> plans);

    /// <summary>Pushes the latest tooltip cards to the popup.</summary>
    void ShowCards(IReadOnlyList<TooltipCard> cards);

    /// <summary>Shows a balloon notification at the given severity.</summary>
    void Notify(NotificationLevel level, string message);
}
