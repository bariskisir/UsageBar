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
    [property: JsonPropertyName("ELEVENLABS_API_KEY")] string? ElevenLabsApiKey,
    [property: JsonPropertyName("telegram")] TelegramSettings? Telegram,
    [property: JsonPropertyName("discord")] DiscordSettings? Discord)
{
    /// <summary>The built-in defaults used when no settings file exists yet.</summary>
    public static AppSettings Default { get; } =
        new(5, 70, 95, string.Empty, string.Empty, string.Empty, string.Empty, null, null);

    /// <summary>Maximum allowed refresh period in minutes (24 hours).</summary>
    private const int MaxRefreshPeriodMinutes = 1440;

    /// <summary>
    /// Returns a copy with out-of-range numeric values reset to defaults, null
    /// credential keys replaced with empty strings, and cross-field constraints
    /// enforced (HighPercentage must be less than CriticalPercentage).
    /// </summary>
    public AppSettings Normalize()
    {
        var normalized = this with
        {
            RefreshPeriodMinute = RefreshPeriodMinute is > 0 and <= MaxRefreshPeriodMinutes ? RefreshPeriodMinute : Default.RefreshPeriodMinute,
            HighPercentage = HighPercentage is >= 1 and <= 100 ? HighPercentage : Default.HighPercentage,
            CriticalPercentage = CriticalPercentage is >= 1 and <= 100 ? CriticalPercentage : Default.CriticalPercentage,
            DeepSeekApiKey = DeepSeekApiKey ?? string.Empty,
            OpenRouterApiKey = OpenRouterApiKey ?? string.Empty,
            DeepgramApiKey = DeepgramApiKey ?? string.Empty,
            ElevenLabsApiKey = ElevenLabsApiKey ?? string.Empty,
            Telegram = Telegram ?? TelegramSettings.Default,
            Discord = Discord ?? DiscordSettings.Default,
        };

        // Enforce HighPercentage < CriticalPercentage after all individual fields are valid.
        // If the high threshold is not strictly below the critical threshold, nudge the critical
        // threshold up (or high down) so the levels are distinct — otherwise the high
        // notification would never fire.
        if (normalized.HighPercentage >= normalized.CriticalPercentage)
        {
            var adjusted = Math.Min(100, normalized.HighPercentage + 10);
            if (adjusted > normalized.HighPercentage)
            {
                normalized = normalized with { CriticalPercentage = adjusted };
            }
            else
            {
                // Both values are at or near 100 — nudge High down instead.
                normalized = normalized with { HighPercentage = Math.Max(1, normalized.CriticalPercentage - 10) };
            }
        }

        return normalized;
    }

    /// <summary>
    /// Returns a string that redacts all API keys and secrets so this record is safe to
    /// include in logs, crash dumps, and debug output.
    /// </summary>
    public override string ToString() =>
        $"AppSettings {{ RefreshPeriodMinute = {RefreshPeriodMinute}, HighPercentage = {HighPercentage}, CriticalPercentage = {CriticalPercentage}, DeepSeekApiKey = ***, OpenRouterApiKey = ***, DeepgramApiKey = ***, ElevenLabsApiKey = ***, Telegram = {(Telegram is not null ? "***" : "null")}, Discord = {(Discord is not null ? "***" : "null")} }}";
}
