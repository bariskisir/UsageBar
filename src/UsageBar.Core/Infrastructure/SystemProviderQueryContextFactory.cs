using UsageBar.Core.Application;
using UsageBar.Core.Configuration;
using UsageBar.Core.Providers;

namespace UsageBar.Core.Infrastructure;

internal sealed class SystemProviderQueryContextFactory : IProviderQueryContextFactory
{
    public ProviderQueryContext Create(AppSettings settings, DateTimeOffset now) =>
        ProviderQueryContext.FromSettings(settings, now, Environment.GetEnvironmentVariable);
}
