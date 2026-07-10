using UsageBar.Core.Domain;

namespace UsageBar.Core.Application;

internal static class NotificationMessageFormatter
{
    public static string Format(NotificationLevel level, string raw)
    {
        var emoji = level switch
        {
            NotificationLevel.Critical => "\u26a0\ufe0f ",
            NotificationLevel.High => "\u26a1 ",
            NotificationLevel.Reset => "\u2705 ",
            _ => string.Empty,
        };

        return $"{emoji}{raw}";
    }
}
