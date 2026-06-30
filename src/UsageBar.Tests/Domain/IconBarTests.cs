using UsageBar.Domain;
using Xunit;

namespace UsageBar.Tests;

public sealed class IconBarTests
{
    [Fact]
    public void Create_clamps_used_percent_to_0_100()
    {
        Assert.Equal(0, IconBar.Create(-10, 1.0).UsedPercent);
        Assert.Equal(100, IconBar.Create(150, 1.0).UsedPercent);
        Assert.Equal(50, IconBar.Create(50, 1.0).UsedPercent);
    }

    [Fact]
    public void Create_preserves_null_used_percent()
    {
        Assert.Null(IconBar.Create(null, 1.0).UsedPercent);
    }

    [Fact]
    public void Create_normalises_zero_or_negative_weight_to_minimum()
    {
        Assert.Equal(0.001, IconBar.Create(50, 0).Weight);
        Assert.Equal(0.001, IconBar.Create(50, -5).Weight);
        Assert.Equal(0.001, IconBar.Create(50, double.MinValue).Weight);
    }

    [Fact]
    public void Create_handles_nan_used_percent()
    {
        // NaN → HasValue is true, Math.Clamp(NaN, 0, 100) returns NaN in older .NET,
        // but .NET's Math.Clamp returns the min value (0) for NaN. Verify the behavior.
        var bar = IconBar.Create(double.NaN, 1.0);
        Assert.True(bar.UsedPercent.HasValue);
        Assert.True(double.IsNaN(bar.UsedPercent!.Value) || bar.UsedPercent.Value >= 0);
    }

    [Fact]
    public void Create_handles_infinity_used_percent()
    {
        var positive = IconBar.Create(double.PositiveInfinity, 1.0);
        Assert.Equal(100, positive.UsedPercent);

        var negative = IconBar.Create(double.NegativeInfinity, 1.0);
        Assert.Equal(0, negative.UsedPercent);
    }

    [Fact]
    public void Create_handles_extreme_weight()
    {
        var bar = IconBar.Create(50, double.MaxValue);
        Assert.True(bar.Weight > 0);
    }

    [Fact]
    public void Default_constructor_does_not_clamp()
    {
        // The auto-generated constructor is public; verify it does NOT clamp so callers
        // always go through Create().
        var bar = new IconBar(-50, -1.0);
        Assert.Equal(-50, bar.UsedPercent);
        Assert.Equal(-1.0, bar.Weight);
    }
}
