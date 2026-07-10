namespace UsageBar.Core.Application;

public sealed record UsageRefreshOptions(bool ForceAutomaticIconLayout)
{
    public static UsageRefreshOptions Default { get; } = new(false);
}
