using UsageBar.Application;
using UsageBar.Configuration;
using UsageBar.Domain;
using Xunit;

namespace UsageBar.Tests;

public sealed class ThresholdNotificationDispatcherTests
{
    [Fact]
    public async Task Emits_grouped_notifications_in_severity_order()
    {
        var view = new RecordingUsageView();
        var remote = new RecordingRemoteNotificationService();
        var dispatcher = new ThresholdNotificationDispatcher(view, [remote]);

        await dispatcher.EmitAsync(
            [TestData.Window("Codex", "Session", 60), TestData.Window("Claude", "Weekly", 60)],
            AppSettings.Default);

        await dispatcher.EmitAsync(
            [TestData.Window("Codex", "Session", 95), TestData.Window("Claude", "Weekly", 75)],
            AppSettings.Default);

        Assert.Equal([NotificationLevel.Critical, NotificationLevel.High], view.Notifications.Select(n => n.Level));
        Assert.Contains("Codex Session", view.Notifications[0].Message, StringComparison.Ordinal);
        Assert.Contains("Claude Weekly", view.Notifications[1].Message, StringComparison.Ordinal);
        Assert.Equal(view.Notifications.Select(n => n.Message), remote.Messages);
    }

    [Fact]
    public void SendTestNotification_notifies_view_and_remote_services()
    {
        var view = new RecordingUsageView();
        var remote = new RecordingRemoteNotificationService();
        var dispatcher = new ThresholdNotificationDispatcher(view, [remote]);

        dispatcher.SendTestNotification();

        var notification = Assert.Single(view.Notifications);
        Assert.Equal(NotificationLevel.Critical, notification.Level);
        Assert.Contains("Test: Limit reached 100%", notification.Message, StringComparison.Ordinal);
        Assert.Equal([notification.Message], remote.Messages);
    }

    private sealed class RecordingUsageView : IUsageView
    {
        public List<(NotificationLevel Level, string Message)> Notifications { get; } = [];

        public void ShowIcon(IReadOnlyList<IconLayout.Bar> bars)
        {
        }

        public void ShowCards(IReadOnlyList<TooltipCard> cards)
        {
        }

        public void Notify(NotificationLevel level, string message) => Notifications.Add((level, message));
    }

    private sealed class RecordingRemoteNotificationService : IRemoteNotificationService
    {
        public List<string> Messages { get; } = [];

        public Task SendAsync(string message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
