using System.Text.Json;
using System.Text.Json.Serialization;
using UsageBar.Domain;
using UsageBar.Infrastructure.Diagnostics;

namespace UsageBar.Infrastructure.Configuration;

internal sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsFilePath;
    private readonly AppLogger _logger;

    public SettingsService(string settingsFilePath, AppLogger logger)
    {
        _settingsFilePath = settingsFilePath;
        _logger = logger;
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
        EnsureSettingsFile();
    }

    public async Task<AppSettings> ReadAsync()
    {
        try
        {
            EnsureSettingsFile();
            AppSettings? settings;

            await using (var stream = File.OpenRead(_settingsFilePath))
            {
                settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions).ConfigureAwait(false);
            }

            var normalizedSettings = settings?.Normalize() ?? AppSettings.Default;
            await WriteAsync(normalizedSettings).ConfigureAwait(false);
            return normalizedSettings;
        }
        catch (Exception exception)
        {
            await _logger.LogAsync("Failed to read settings.json. Using defaults.", exception).ConfigureAwait(false);
            return AppSettings.Default;
        }
    }

    private void EnsureSettingsFile()
    {
        if (File.Exists(_settingsFilePath))
        {
            return;
        }

        var json = JsonSerializer.Serialize(AppSettings.Default, JsonOptions);
        File.WriteAllText(_settingsFilePath, json);
    }

    /// <summary>
    /// Synchronous read for use from the UI thread (context menu commands).
    /// </summary>
    public AppSettings ReadSync()
    {
        try
        {
            EnsureSettingsFile();
            var json = File.ReadAllText(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return (settings ?? AppSettings.Default).Normalize();
        }
        catch
        {
            return AppSettings.Default;
        }
    }

    /// <summary>
    /// Synchronous write for use from the UI thread (context menu commands).
    /// </summary>
    public void WriteSync(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Best-effort; the next async read will repair.
        }
    }

    private async Task WriteAsync(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(_settingsFilePath, json).ConfigureAwait(false);
    }
}

internal sealed record AppSettings(
    [property: JsonPropertyName("refreshPeriodMinute")] int RefreshPeriodMinute,
    [property: JsonPropertyName("highPercentage")] double HighPercentage,
    [property: JsonPropertyName("criticalPercentage")] double CriticalPercentage,
    [property: JsonPropertyName("useLegacyTooltip")] bool UseLegacyTooltip,
    [property: JsonPropertyName("DEEPSEEK_API_KEY")] string? DeepSeekApiKey,
    [property: JsonPropertyName("OPENROUTER_API_KEY")] string? OpenRouterApiKey,
    [property: JsonPropertyName("DEEPGRAM_API_KEY")] string? DeepgramApiKey)
{
    public static AppSettings Default { get; } = new(5, 70, 90, false, string.Empty, string.Empty, string.Empty);

    public AppSettings Normalize()
    {
        return this with
        {
            RefreshPeriodMinute = RefreshPeriodMinute > 0 ? RefreshPeriodMinute : Default.RefreshPeriodMinute,
            HighPercentage = HighPercentage is >= 1 and <= 100 ? HighPercentage : Default.HighPercentage,
            CriticalPercentage = CriticalPercentage is >= 1 and <= 100 ? CriticalPercentage : Default.CriticalPercentage,
            DeepSeekApiKey = DeepSeekApiKey ?? string.Empty,
            DeepgramApiKey = DeepgramApiKey ?? string.Empty,
            OpenRouterApiKey = OpenRouterApiKey ?? string.Empty
        };
    }

    public ProviderCredentials ToProviderCredentials()
    {
        return new ProviderCredentials(
            ResolveCredential(DeepSeekApiKey, "DEEPSEEK_API_KEY"),
            ResolveCredential(OpenRouterApiKey, "OPENROUTER_API_KEY"),
            ResolveCredential(DeepgramApiKey, "DEEPGRAM_API_KEY"));
    }

    private static string ResolveCredential(string? settingsValue, string environmentVariableName)
    {
        if (!string.IsNullOrWhiteSpace(settingsValue))
        {
            return settingsValue;
        }

        return Environment.GetEnvironmentVariable(environmentVariableName) ?? string.Empty;
    }
}
