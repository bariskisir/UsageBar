using UsageBar.Configuration;
using UsageBar.Domain;
using System.Text.Json;
using Xunit;

namespace UsageBar.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void Default_has_expected_values()
    {
        var defaults = AppSettings.Default;
        Assert.Equal(5, defaults.RefreshPeriodMinute);
        Assert.Equal(70, defaults.HighPercentage);
        Assert.Equal(95, defaults.CriticalPercentage);
        Assert.Equal(TrayIconLayoutSettings.AutoMode, defaults.IconLayout!.Mode);
    }

    [Fact]
    public void Normalize_repairs_out_of_range_values()
    {
        var settings = new AppSettings(
            RefreshPeriodMinute: 0,
            HighPercentage: 0,
            CriticalPercentage: 150,
            DeepSeekApiKey: null,
            OpenRouterApiKey: null,
            MoonshotApiKey: null,
            DeepgramApiKey: null,
            ElevenLabsApiKey: null,
            KiloApiKey: null,
            OpenAiApiKey: null,
            VeniceApiKey: null,
            CopilotApiKey: null,
            CrofApiKey: null,
            CodebuffApiKey: null,
            WarpApiKey: null,
            ZaiApiKey: null,
            SyntheticApiKey: null,
            ChutesApiKey: null,
            MiniMaxApiKey: null,
            PoeApiKey: null,
            AlibabaApiKey: null,
            IconLayout: null,
            Telegram: null,
            Discord: null,
            HiddenProviders: null,
            CheckUpdatesOnStartup: null);

        var normalized = settings.Normalize();

        Assert.Equal(5, normalized.RefreshPeriodMinute);
        Assert.Equal(70, normalized.HighPercentage);
        Assert.Equal(95, normalized.CriticalPercentage);
        Assert.Equal(string.Empty, normalized.DeepSeekApiKey);
        Assert.Equal(string.Empty, normalized.OpenRouterApiKey);
        Assert.Equal(string.Empty, normalized.MoonshotApiKey);
        Assert.Equal(string.Empty, normalized.DeepgramApiKey);
        Assert.Equal(string.Empty, normalized.ElevenLabsApiKey);
        Assert.Equal(string.Empty, normalized.KiloApiKey);
        Assert.Equal(TrayIconLayoutSettings.AutoMode, normalized.IconLayout!.Mode);
        Assert.NotNull(normalized.Telegram);
        Assert.Null(normalized.Telegram!.Token);
        Assert.Equal(0, normalized.Telegram.ChatId);
        Assert.NotNull(normalized.Discord);
        Assert.Null(normalized.Discord!.WebhookUrl);
        Assert.Equal("Usage Bar", normalized.Discord.Username);
    }

    [Fact]
    public void Normalize_keeps_valid_values()
    {
        var iconLayout = new TrayIconLayoutSettings(
            TrayIconLayoutSettings.ManualMode,
            new Dictionary<string, double> { ["codex_session"] = 25 });
        var settings = new AppSettings(15, 60, 85, "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p", "q", "r", iconLayout, null, null, null, null);

        var normalized = settings.Normalize();

        Assert.Equal(15, normalized.RefreshPeriodMinute);
        Assert.Equal(60, normalized.HighPercentage);
        Assert.Equal(85, normalized.CriticalPercentage);
        Assert.Equal("a", normalized.DeepSeekApiKey);
        Assert.Equal("c", normalized.MoonshotApiKey);
        Assert.Equal("e", normalized.ElevenLabsApiKey);
        Assert.Equal("f", normalized.KiloApiKey);
        Assert.True(normalized.IconLayout!.IsManual);
        Assert.Equal(25, normalized.IconLayout.Bars!["codex_session"]);
    }

    [Fact]
    public void Icon_layout_serialization_does_not_emit_computed_is_manual_property()
    {
        var settings = new TrayIconLayoutSettings(
            TrayIconLayoutSettings.ManualMode,
            new Dictionary<string, double> { ["codex_session"] = 10 });

        var json = JsonSerializer.Serialize(settings);

        Assert.Contains("\"mode\":\"manual\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("IsManual", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_settings_serialize_icon_layout_as_auto()
    {
        var json = JsonSerializer.Serialize(AppSettings.Default);

        Assert.Contains("\"iconLayout\"", json, StringComparison.Ordinal);
        Assert.Contains("\"mode\":\"auto\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"mode\":\"default\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_caps_refresh_period_at_maximum()
    {
        var settings = AppSettings.Default with { RefreshPeriodMinute = 10080 };

        var normalized = settings.Normalize();

        Assert.Equal(5, normalized.RefreshPeriodMinute);
    }

    [Fact]
    public void Normalize_allows_maximum_refresh_period()
    {
        var settings = AppSettings.Default with { RefreshPeriodMinute = 1440 };

        var normalized = settings.Normalize();

        Assert.Equal(1440, normalized.RefreshPeriodMinute);
    }

    [Fact]
    public void Normalize_nudges_high_down_when_both_at_100()
    {
        // When both thresholds are clamped at 100 the Normalize logic cannot nudge
        // Critical up (it's already at the ceiling), so it must nudge High down.
        var settings = AppSettings.Default with { HighPercentage = 100, CriticalPercentage = 100 };

        var normalized = settings.Normalize();

        Assert.Equal(90, normalized.HighPercentage);
        Assert.Equal(100, normalized.CriticalPercentage);
    }

    [Fact]
    public void Normalize_nudges_critical_up_when_high_equals_critical()
    {
        var settings = AppSettings.Default with { HighPercentage = 80, CriticalPercentage = 80 };

        var normalized = settings.Normalize();

        Assert.Equal(80, normalized.HighPercentage);
        Assert.Equal(90, normalized.CriticalPercentage);
        Assert.True(normalized.HighPercentage < normalized.CriticalPercentage);
    }

    [Fact]
    public void Normalize_nudges_critical_up_capped_at_100()
    {
        var settings = AppSettings.Default with { HighPercentage = 95, CriticalPercentage = 95 };

        var normalized = settings.Normalize();

        Assert.Equal(95, normalized.HighPercentage);
        Assert.Equal(100, normalized.CriticalPercentage);
    }

    [Fact]
    public void Normalize_clamps_high_at_1_minimum()
    {
        var settings = AppSettings.Default with { HighPercentage = 0, CriticalPercentage = 5 };

        var normalized = settings.Normalize();

        Assert.Equal(70, normalized.HighPercentage); // 0 is out of range → default
    }
}
