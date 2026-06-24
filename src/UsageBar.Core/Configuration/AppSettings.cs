using System.Text.Json.Serialization;
using UsageBar.Domain;

namespace UsageBar.Configuration;

/// <summary>
/// User-configurable application settings, persisted to
/// <c>%APPDATA%\UsageBar\settings.json</c>. JSON keys are stable for backward
/// compatibility with existing settings files.
/// </summary>
public sealed record AppSettings(
    [property: JsonPropertyName("refreshPeriodMinute")] int RefreshPeriodMinute,
    [property: JsonPropertyName("highPercentage")] double HighPercentage,
    [property: JsonPropertyName("criticalPercentage")] double CriticalPercentage,
    [property: JsonPropertyName("DEEPSEEK_API_KEY")] string? DeepSeekApiKey,
    [property: JsonPropertyName("OPENROUTER_API_KEY")] string? OpenRouterApiKey,
    [property: JsonPropertyName("DEEPGRAM_API_KEY")] string? DeepgramApiKey,
    [property: JsonPropertyName("telegram")] TelegramSettings? Telegram,
    [property: JsonPropertyName("discord")] DiscordSettings? Discord)
{
    /// <summary>The built-in defaults used when no settings file exists yet.</summary>
    public static AppSettings Default { get; } =
        new(5, 70, 90, string.Empty, string.Empty, string.Empty, null, null);

    /// <summary>
    /// Returns a copy with out-of-range numeric values reset to defaults and null
    /// credential keys replaced with empty strings.
    /// </summary>
    public AppSettings Normalize() => this with
    {
        RefreshPeriodMinute = RefreshPeriodMinute > 0 ? RefreshPeriodMinute : Default.RefreshPeriodMinute,
        HighPercentage = HighPercentage is >= 1 and <= 100 ? HighPercentage : Default.HighPercentage,
        CriticalPercentage = CriticalPercentage is >= 1 and <= 100 ? CriticalPercentage : Default.CriticalPercentage,
        DeepSeekApiKey = DeepSeekApiKey ?? string.Empty,
        OpenRouterApiKey = OpenRouterApiKey ?? string.Empty,
        DeepgramApiKey = DeepgramApiKey ?? string.Empty,
        Telegram = Telegram ?? new TelegramSettings(null, 0),
        Discord = Discord ?? DiscordSettings.Default,
    };
}
