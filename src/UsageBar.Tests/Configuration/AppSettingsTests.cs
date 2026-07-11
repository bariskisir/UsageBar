using System.Text.Json;
using UsageBar.Core.Configuration;
using UsageBar.Core.Domain;
using Xunit;

namespace UsageBar.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void Default_has_expected_values()
    {
        var defaults = AppSettings.Default;
        Assert.Equal(5, defaults.Refresh!.Minute);
        Assert.Equal(70, defaults.Notification!.High);
        Assert.Equal(90, defaults.Notification!.Critical);
        Assert.Equal(100, defaults.Visual!.Scale);
        Assert.Equal(TrayIconLayoutSettings.AutoMode, defaults.Visual!.IconLayout!.Mode);
        Assert.Null(defaults.Providers);
        Assert.False(defaults.Initialized);
        Assert.Equal(AppSettings.CurrentSchemaVersion, defaults.SchemaVersion);
    }

    [Fact]
    public void Provider_descriptor_derives_stable_id_independently_from_display_formatting()
    {
        var descriptor = new UsageBar.Core.Providers.ProviderDescriptor("Moonshot (Kimi)", 0);

        Assert.Equal("moonshotkimi", descriptor.Id);
    }

    [Fact]
    public void Normalize_repairs_out_of_range_values()
    {
        var settings = AppSettings.Default with
        {
            Refresh = new RefreshSettings(0),
            Notification = new NotificationSettings(0, 150, null, null),
            Visual = null,
            Initialized = null,
        };

        var normalized = settings.Normalize();
        Assert.Equal(5, normalized.Refresh!.Minute);
        Assert.Equal(70, normalized.Notification!.High);
        Assert.Equal(90, normalized.Notification!.Critical);
        Assert.Equal(TrayIconLayoutSettings.AutoMode, normalized.Visual!.IconLayout!.Mode);
        Assert.NotNull(normalized.Notification!.Telegram);
        Assert.Null(normalized.Notification!.Telegram!.Token);
        Assert.Equal(0, normalized.Notification!.Telegram!.ChatId);
        Assert.NotNull(normalized.Notification!.Discord);
        Assert.Null(normalized.Notification!.Discord!.WebhookUrl);
        Assert.Equal("Usage Bar", normalized.Notification!.Discord!.Username);
    }

    [Fact]
    public void Normalize_keeps_valid_values()
    {
        var iconLayout = new TrayIconLayoutSettings(TrayIconLayoutSettings.ManualMode,
            new Dictionary<string, double> { ["codex_session"] = 25 });
        var settings = AppSettings.Default with
        {
            Refresh = new RefreshSettings(15),
            Notification = new NotificationSettings(60, 85, null, null),
            Visual = new VisualSettings(Scale: 100, IconLayout: iconLayout),
        };

        var normalized = settings.Normalize();
        Assert.Equal(15, normalized.Refresh!.Minute);
        Assert.Equal(60, normalized.Notification!.High);
        Assert.Equal(85, normalized.Notification!.Critical);
        Assert.True(normalized.Visual!.IconLayout!.IsManual);
        Assert.Equal(25, normalized.Visual!.IconLayout!.Bars!["codex_session"]);
    }

    [Fact]
    public void Normalize_clamps_scale_to_valid_range()
    {
        var defaultVisual = AppSettings.Default.Visual!;
        Assert.Equal(100, defaultVisual.Scale);

        var clamped = (AppSettings.Default with
        {
            Visual = defaultVisual with { Scale = 60 }
        }).Normalize();
        Assert.Equal(100, clamped.Visual!.Scale);

        clamped = (AppSettings.Default with
        {
            Visual = defaultVisual with { Scale = 137 }
        }).Normalize();
        Assert.Equal(137, clamped.Visual!.Scale);

        clamped = (AppSettings.Default with
        {
            Visual = defaultVisual with { Scale = 30 }
        }).Normalize();
        Assert.Equal(100, clamped.Visual!.Scale);

        clamped = (AppSettings.Default with
        {
            Visual = defaultVisual with { Scale = 200 }
        }).Normalize();
        Assert.Equal(150, clamped.Visual!.Scale);

        clamped = (AppSettings.Default with
        {
            Visual = defaultVisual with { Scale = 87 }
        }).Normalize();
        Assert.Equal(100, clamped.Visual!.Scale);
    }

    [Fact]
    public void Icon_layout_serialization_does_not_emit_computed_is_manual_property()
    {
        var settings = new TrayIconLayoutSettings(TrayIconLayoutSettings.ManualMode,
            new Dictionary<string, double> { ["codex_session"] = 10 });
        var json = JsonSerializer.Serialize(settings);
        Assert.DoesNotContain("isManual", json);
        Assert.Contains("\"mode\"", json);
    }

    [Fact]
    public void AppSettings_serialization_includes_providers()
    {
        var settings = AppSettings.Default with
        {
            Providers =
            [
                new ProviderSettings("Codex", ProviderSettings.TypeOAuth, null, null, Enabled: true),
                new ProviderSettings("DeepSeek", ProviderSettings.TypeApiKey, "DEEPSEEK_API_KEY", "sk-test", Enabled: true),
            ],
        };
        var json = JsonSerializer.Serialize(settings,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.Contains("\"providers\"", json);
        Assert.Contains("\"Codex\"", json);
    }
}