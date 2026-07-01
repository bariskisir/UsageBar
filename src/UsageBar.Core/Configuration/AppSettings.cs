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
    [property: JsonPropertyName("MOONSHOT_API_KEY")] string? MoonshotApiKey,
    [property: JsonPropertyName("DEEPGRAM_API_KEY")] string? DeepgramApiKey,
    [property: JsonPropertyName("ELEVENLABS_API_KEY")] string? ElevenLabsApiKey,
    [property: JsonPropertyName("KILO_API_KEY")] string? KiloApiKey,
    [property: JsonPropertyName("OPENAI_API_KEY")] string? OpenAiApiKey,
    [property: JsonPropertyName("VENICE_API_KEY")] string? VeniceApiKey,
    [property: JsonPropertyName("COPILOT_API_KEY")] string? CopilotApiKey,
    [property: JsonPropertyName("CROF_API_KEY")] string? CrofApiKey,
    [property: JsonPropertyName("CODEBUFF_API_KEY")] string? CodebuffApiKey,
    [property: JsonPropertyName("WARP_API_KEY")] string? WarpApiKey,
    [property: JsonPropertyName("ZAI_API_KEY")] string? ZaiApiKey,
    [property: JsonPropertyName("SYNTHETIC_API_KEY")] string? SyntheticApiKey,
    [property: JsonPropertyName("CHUTES_API_KEY")] string? ChutesApiKey,
    [property: JsonPropertyName("MINIMAX_API_KEY")] string? MiniMaxApiKey,
    [property: JsonPropertyName("POE_API_KEY")] string? PoeApiKey,
    [property: JsonPropertyName("ALIBABA_API_KEY")] string? AlibabaApiKey,
    [property: JsonPropertyName("iconLayout")] TrayIconLayoutSettings? IconLayout,
    [property: JsonPropertyName("balanceHidingThreshold")] double? BalanceHidingThreshold,
    [property: JsonPropertyName("telegram")] TelegramSettings? Telegram,
    [property: JsonPropertyName("discord")] DiscordSettings? Discord,
    [property: JsonPropertyName("checkUpdatesOnStartup")] bool? CheckUpdatesOnStartup)
{
    /// <summary>The built-in defaults used when no settings file exists yet.</summary>
    public static AppSettings Default { get; } =
        new(
            RefreshPeriodMinute: 5,
            HighPercentage: 70,
            CriticalPercentage: 95,
            DeepSeekApiKey: string.Empty,
            OpenRouterApiKey: string.Empty,
            MoonshotApiKey: string.Empty,
            DeepgramApiKey: string.Empty,
            ElevenLabsApiKey: string.Empty,
            KiloApiKey: string.Empty,
            OpenAiApiKey: string.Empty,
            VeniceApiKey: string.Empty,
            CopilotApiKey: string.Empty,
            CrofApiKey: string.Empty,
            CodebuffApiKey: string.Empty,
            WarpApiKey: string.Empty,
            ZaiApiKey: string.Empty,
            SyntheticApiKey: string.Empty,
            ChutesApiKey: string.Empty,
            MiniMaxApiKey: string.Empty,
            PoeApiKey: string.Empty,
            AlibabaApiKey: string.Empty,
            IconLayout: TrayIconLayoutSettings.Default,
            BalanceHidingThreshold: -1,
            Telegram: null,
            Discord: null,
            CheckUpdatesOnStartup: true);

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
            MoonshotApiKey = MoonshotApiKey ?? string.Empty,
            DeepgramApiKey = DeepgramApiKey ?? string.Empty,
            ElevenLabsApiKey = ElevenLabsApiKey ?? string.Empty,
            KiloApiKey = KiloApiKey ?? string.Empty,
            OpenAiApiKey = OpenAiApiKey ?? string.Empty,
            VeniceApiKey = VeniceApiKey ?? string.Empty,
            CopilotApiKey = CopilotApiKey ?? string.Empty,
            CrofApiKey = CrofApiKey ?? string.Empty,
            CodebuffApiKey = CodebuffApiKey ?? string.Empty,
            WarpApiKey = WarpApiKey ?? string.Empty,
            ZaiApiKey = ZaiApiKey ?? string.Empty,
            SyntheticApiKey = SyntheticApiKey ?? string.Empty,
            ChutesApiKey = ChutesApiKey ?? string.Empty,
            MiniMaxApiKey = MiniMaxApiKey ?? string.Empty,
            PoeApiKey = PoeApiKey ?? string.Empty,
            AlibabaApiKey = AlibabaApiKey ?? string.Empty,
            BalanceHidingThreshold = BalanceHidingThreshold is { } threshold && double.IsFinite(threshold) ? threshold : Default.BalanceHidingThreshold,
            IconLayout = (IconLayout ?? TrayIconLayoutSettings.Default).Normalize(),
            Telegram = Telegram ?? TelegramSettings.Default,
            Discord = Discord ?? DiscordSettings.Default,
            CheckUpdatesOnStartup = CheckUpdatesOnStartup ?? true,
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
        $"AppSettings {{ RefreshPeriodMinute = {RefreshPeriodMinute}, HighPercentage = {HighPercentage}, CriticalPercentage = {CriticalPercentage}, BalanceHidingThreshold = {BalanceHidingThreshold}, DeepSeekApiKey = ***, OpenRouterApiKey = ***, MoonshotApiKey = ***, DeepgramApiKey = ***, ElevenLabsApiKey = ***, KiloApiKey = ***, OpenAiApiKey = ***, VeniceApiKey = ***, CopilotApiKey = ***, CrofApiKey = ***, CodebuffApiKey = ***, WarpApiKey = ***, ZaiApiKey = ***, SyntheticApiKey = ***, ChutesApiKey = ***, MiniMaxApiKey = ***, PoeApiKey = ***, AlibabaApiKey = ***, Telegram = {(Telegram is not null ? "***" : "null")}, Discord = {(Discord is not null ? "***" : "null")}, CheckUpdatesOnStartup = {CheckUpdatesOnStartup} }}";
}

/// <summary>
/// User-configurable tray icon layout. Auto mode shows every metric window equally in
/// provider display order. Manual mode shows only the configured window keys, in JSON order,
/// using each value as the bar height percentage.
/// </summary>
public sealed record TrayIconLayoutSettings(
    [property: JsonPropertyName("mode")] string? Mode,
    [property: JsonPropertyName("bars")] Dictionary<string, double>? Bars)
{
    public const string AutoMode = "auto";
    public const string ManualMode = "manual";

    public static TrayIconLayoutSettings Default { get; } = new(AutoMode, []);

    [JsonIgnore]
    public bool IsManual => string.Equals(Mode, ManualMode, StringComparison.OrdinalIgnoreCase);

    public TrayIconLayoutSettings Normalize()
    {
        var mode = string.Equals(Mode, ManualMode, StringComparison.OrdinalIgnoreCase)
            ? ManualMode
            : AutoMode;

        var bars = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (Bars is not null)
        {
            foreach (var (key, value) in Bars)
            {
                if (!string.IsNullOrWhiteSpace(key) && double.IsFinite(value) && value > 0)
                {
                    bars[key.Trim()] = value;
                }
            }
        }

        return new TrayIconLayoutSettings(mode, bars);
    }
}
