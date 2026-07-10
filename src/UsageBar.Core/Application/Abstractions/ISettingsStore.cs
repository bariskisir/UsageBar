using UsageBar.Core.Configuration;

namespace UsageBar.Core.Application;

/// <summary>Reads and writes persisted <see cref="AppSettings"/>.</summary>
public interface ISettingsStore
{
    /// <summary>Reads and normalises settings (used by the background refresh loop).</summary>
    Task<AppSettings> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Atomically persists settings.</summary>
    Task WriteAsync(AppSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Reads and normalises settings synchronously (used by the UI thread).</summary>
    AppSettings Read();

    /// <summary>Persists settings (used by the context-menu UI thread).</summary>
    void Write(AppSettings settings);
}
