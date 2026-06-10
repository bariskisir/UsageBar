using UsageBar.Configuration;
using UsageBar.Domain;
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
        Assert.Equal(90, defaults.CriticalPercentage);
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
            DeepgramApiKey: null,
            Telegram: null);

        var normalized = settings.Normalize();

        Assert.Equal(5, normalized.RefreshPeriodMinute);
        Assert.Equal(70, normalized.HighPercentage);
        Assert.Equal(90, normalized.CriticalPercentage);
        Assert.Equal(string.Empty, normalized.DeepSeekApiKey);
        Assert.Equal(string.Empty, normalized.OpenRouterApiKey);
        Assert.Equal(string.Empty, normalized.DeepgramApiKey);
        Assert.NotNull(normalized.Telegram);
        Assert.Null(normalized.Telegram!.Token);
        Assert.Equal(0, normalized.Telegram.ChatId);
    }

    [Fact]
    public void Normalize_keeps_valid_values()
    {
        var settings = new AppSettings(15, 60, 85, "a", "b", "c", null);

        var normalized = settings.Normalize();

        Assert.Equal(15, normalized.RefreshPeriodMinute);
        Assert.Equal(60, normalized.HighPercentage);
        Assert.Equal(85, normalized.CriticalPercentage);
        Assert.Equal("a", normalized.DeepSeekApiKey);
    }
}
