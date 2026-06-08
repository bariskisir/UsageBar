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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _filePath;
    private readonly ILogger<JsonSettingsStore> _logger;

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
            EnsureFile();

            AppSettings? settings;
            await using (var stream = File.OpenRead(_filePath))
            {
                settings = await JsonSerializer
                    .DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            var normalized = settings?.Normalize() ?? AppSettings.Default;
            await File.WriteAllTextAsync(_filePath, Serialize(normalized), cancellationToken).ConfigureAwait(false);
            return normalized;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to read settings.json; using defaults.");
            return AppSettings.Default;
        }
    }

    public AppSettings Read()
    {
        try
        {
            EnsureFile();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath), JsonOptions);
            return (settings ?? AppSettings.Default).Normalize();
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
            File.WriteAllText(_filePath, Serialize(settings));
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

    private static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, JsonOptions);
}
