using System.Text.Json;
using Microsoft.Extensions.Logging;
using UsageBar.Application;
using UsageBar.Configuration;

namespace UsageBar.Infrastructure;

/// <summary>
/// <see cref="ISettingsStore"/> backed by a JSON file. Creates the file with defaults when
/// missing, normalises on read, and supports synchronous read/write for the context-menu UI thread.
/// </summary>
internal sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _filePath;
    private readonly ILogger<JsonSettingsStore> _logger;
    private readonly Lock _gate = new();

    public JsonSettingsStore(string filePath, ILogger<JsonSettingsStore> logger)
    {
        _filePath = filePath;
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        EnsureFile();
    }

    public async Task<AppSettings> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string raw;
            lock (_gate)
            {
                EnsureFile();
                raw = File.ReadAllText(_filePath);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var settings = JsonSerializer.Deserialize(raw, SettingsJsonContext.Default.AppSettings)?.Normalize()
                           ?? AppSettings.Default;
            WriteNormalizedIfChanged(raw, settings);
            return settings;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Failed to read settings.json; using defaults.");
            return AppSettings.Default;
        }
    }

    public AppSettings Read()
    {
        try
        {
            string raw;
            lock (_gate)
            {
                EnsureFile();
                raw = File.ReadAllText(_filePath);
            }

            var settings = JsonSerializer.Deserialize(raw, SettingsJsonContext.Default.AppSettings);
            var normalized = (settings ?? AppSettings.Default).Normalize();
            WriteNormalizedIfChanged(raw, normalized);
            return normalized;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to read settings.json; using defaults.");
            return AppSettings.Default;
        }
    }

    public void Write(AppSettings settings)
    {
        try
        {
            lock (_gate)
            {
                File.WriteAllText(_filePath, Serialize(settings));
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to write settings.json.");
        }
    }

    private void EnsureFile()
    {
        if (!File.Exists(_filePath))
        {
            File.WriteAllText(_filePath, Serialize(AppSettings.Default));
        }
    }

    private static string Serialize(AppSettings settings) =>
        JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);

    private void WriteNormalizedIfChanged(string raw, AppSettings settings)
    {
        var normalized = Serialize(settings);
        if (raw == normalized)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                File.WriteAllText(_filePath, normalized);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to update settings.json with normalized defaults.");
        }
    }
}
