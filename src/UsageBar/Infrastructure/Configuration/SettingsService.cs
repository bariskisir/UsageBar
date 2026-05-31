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

    private async Task WriteAsync(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(_settingsFilePath, json).ConfigureAwait(false);
    }
}

internal sealed record AppSettings(
    [property: JsonPropertyName("refreshPeriodMinute")] int RefreshPeriodMinute,
    [property: JsonPropertyName("DEEPSEEK_API_KEY")] string? DeepSeekApiKey,
    [property: JsonPropertyName("OPENROUTER_API_KEY")] string? OpenRouterApiKey,
    [property: JsonPropertyName("DEEPGRAM_API_KEY")] string? DeepgramApiKey)
{
    public static AppSettings Default { get; } = new(5, string.Empty, string.Empty, string.Empty);

    public AppSettings Normalize()
    {
        return this with
        {
            RefreshPeriodMinute = RefreshPeriodMinute > 0 ? RefreshPeriodMinute : Default.RefreshPeriodMinute,
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
