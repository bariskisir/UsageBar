using UsageBar.Core.Domain;

namespace UsageBar.Core.Providers;

/// <summary>
/// Optional provider hook for providers whose placement depends on the concrete result kind.
/// </summary>
public interface IResultDisplayOrderProvider
{
    int GetDisplayOrder(ProviderResult result);
}