using UsageBar.Core.Providers;
using Xunit;

namespace UsageBar.Tests;

public sealed class UsageFormattingTests
{
    [Theory]
    [InlineData(0, "now")]
    [InlineData(-30, "now")]
    [InlineData(5, "5m")]
    [InlineData(65, "1h 5m")]
    [InlineData(130, "2h 10m")]
    [InlineData(60 * 25, "1d 1h")]
    [InlineData(60 * 24, "1d")]          // exactly 1 day
    [InlineData(60 * 24 + 5, "1d 5m")]    // 1d 5m, hours=0 → shows minutes
    [InlineData(60 * 24 + 65, "1d 1h")]   // 1d 1h 5m → 1d 1h
    [InlineData(60 * 48, "2d")]           // 2 days
    [InlineData(60 * 48 + 30, "2d 30m")]  // 2d 30m, hours=0 → shows minutes
    public void ResetDuration_formats_expected(int minutes, string expected)
    {
        Assert.Equal(expected, UsageFormatting.ResetDuration(TimeSpan.FromMinutes(minutes)));
    }

    [Theory]
    [InlineData(0, "$0.00")]
    [InlineData(12.5, "$12.50")]
    [InlineData(1234.5, "$1234.50")]
    public void Currency_defaults_to_usd(double value, string expected)
    {
        Assert.Equal(expected, UsageFormatting.Currency((decimal)value));
    }

    [Theory]
    [InlineData(0, "¥0.00")]
    [InlineData(100, "¥100.00")]
    public void Currency_accepts_a_custom_symbol(double value, string expected)
    {
        Assert.Equal(expected, UsageFormatting.Currency((decimal)value, "¥"));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("pro", "Pro")]
    [InlineData("pro_lite", "Pro_lite")]
    [InlineData("Team", "Team")]
    public void Capitalize_uppercases_only_the_first_character(string value, string expected)
    {
        Assert.Equal(expected, UsageFormatting.Capitalize(value));
    }
}
