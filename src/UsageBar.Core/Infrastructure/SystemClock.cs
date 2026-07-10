using UsageBar.Core.Application;

namespace UsageBar.Core.Infrastructure;

/// <summary>Default <see cref="IClock"/> backed by the system clock.</summary>
internal sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}
