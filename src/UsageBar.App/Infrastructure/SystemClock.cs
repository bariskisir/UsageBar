using UsageBar.Application;

namespace UsageBar.Infrastructure;

/// <summary>Default <see cref="IClock"/> backed by the system clock.</summary>
internal sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
