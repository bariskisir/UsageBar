using UsageBar.Domain;

namespace UsageBar.Providers;

/// <summary>
/// Optional provider hook for providers whose placement depends on the concrete result kind.
/// </summary>
public interface IResultDisplayOrderProvider
{
    int GetDisplayOrder(ProviderResult result);
}
