using UsageBar.Core.Configuration;

namespace UsageBar.Core.Application;

/// <summary>Reads and writes persisted <see cref="AppSettings"/>.</summary>
public interface ISettingsStore
{
    /// <summary>Reads and normalises settings (used by the background refresh loop).</summary>
    Task<AppSettings> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Atomically persists settings.</summary>
    Task WriteAsync(AppSettings settings, CancellationToken cancellationToken = default);

}