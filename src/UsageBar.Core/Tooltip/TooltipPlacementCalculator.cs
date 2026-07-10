namespace UsageBar.Core.Tooltip;

internal readonly record struct TooltipPlacement(int X, int Y, int Width, int Height, int CornerRadius);

internal static class TooltipPlacementCalculator
{
    public static TooltipPlacement Compute(
        LayoutRect icon,
        LayoutRect workArea,
        int widthCss,
        int heightCss,
        int minHeightCss,
        int cornerRadiusCss,
        double scale)
    {
        var resolvedHeightCss = Math.Max(heightCss, minHeightCss);
        var width = Scaled(widthCss, scale);
        var height = Scaled(resolvedHeightCss, scale);
        var gap = Scaled(8, scale);
        var radius = Scaled(cornerRadiusCss, scale);

        var x = icon.Right - width;
        var y = icon.Top - height - gap;
        if (y < workArea.Top)
        {
            y = icon.Bottom + gap;
        }

        if (x + width > workArea.Right)
        {
            x = workArea.Right - width - 4;
        }

        if (x < workArea.Left)
        {
            x = workArea.Left + 4;
        }

        if (y + height > workArea.Bottom)
        {
            y = workArea.Bottom - height - 4;
        }

        if (y < workArea.Top)
        {
            y = workArea.Top + 4;
        }

        return new TooltipPlacement(x, y, width, height, radius);
    }

    private static int Scaled(int value, double scale) => (int)Math.Round(value * scale);
}
