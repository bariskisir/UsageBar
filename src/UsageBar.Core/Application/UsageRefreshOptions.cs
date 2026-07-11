namespace UsageBar.Core.Application;

public sealed record UsageRefreshOptions(
    bool ForceAutomaticIconLayout,
    TimeSpan ProviderTimeout)
{
    public static UsageRefreshOptions Default { get; } = new(false, TimeSpan.FromSeconds(45));
}