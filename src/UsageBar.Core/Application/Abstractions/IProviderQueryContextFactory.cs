using UsageBar.Core.Configuration;
using UsageBar.Core.Providers;

namespace UsageBar.Core.Application;

public interface IProviderQueryContextFactory
{
    ProviderQueryContext Create(AppSettings settings, DateTimeOffset now);
}