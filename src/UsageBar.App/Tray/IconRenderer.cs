using UsageBar.Application;

namespace UsageBar.Tray;

/// <summary>
/// Renders the tray icon: a dark plate with one or more horizontal usage bars. The bar
/// selection/order/weight is decided upstream by the providers and <see cref="IconLayout"/>
/// (testable); this class turns the laid-out bars into pixels and a native icon handle using the
/// CodexBar palette.
/// </summary>
internal static class IconRenderer
{
    private const int IconSize = 32;

    // CodexBar-style geometry: a 2px transparent margin around a dark plate, bars inset inside.
    private const int PlateInset = 2;
    private const int BarLeft = 4;
    private const int BarRight = IconSize - 4;          // 28
    private const int BarWidth = BarRight - BarLeft;     // 24
    private const int ContentTop = 6;
    private const int ContentBottom = IconSize - 6;      // 26
    private const int ContentHeight = ContentBottom - ContentTop; // 20

    private const int SepSameProvider = 1;
    private const int SepCrossProvider = 2;

    private static readonly (byte R, byte G, byte B) Plate = (60, 60, 70);
    private static readonly (byte R, byte G, byte B) Track = (80, 80, 90);

    /// <summary>Creates a tray icon (HICON) from the laid-out bars (empty → a single empty track).</summary>
    public static nint CreateUsageIcon(IReadOnlyList<IconLayout.Bar> bars) =>
        RenderIcon(AssignBarPositions(bars));

    private enum UsageLevel
    {
        Low,      // < 50%
        Medium,   // < 80%
        High,     // < 95%
        Critical, // >= 95%
    }

    private readonly record struct BarSpec(int Y, int Height, double? UsedPercent);

    private static UsageLevel LevelFromPercent(double percent) => percent switch
    {
        < 50.0 => UsageLevel.Low,
        < 80.0 => UsageLevel.Medium,
        < 95.0 => UsageLevel.High,
        _ => UsageLevel.Critical,
    };

    private static (byte R, byte G, byte B) LevelColor(UsageLevel level) => level switch
    {
        UsageLevel.Low => (76, 175, 80),       // Green  #4CAF50
        UsageLevel.Medium => (255, 193, 7),     // Amber  #FFC107
        UsageLevel.High => (255, 152, 0),       // Orange #FF9800
        UsageLevel.Critical => (244, 67, 54),   // Red    #F44336
        _ => (76, 175, 80),
    };

    private static List<BarSpec> AssignBarPositions(IReadOnlyList<IconLayout.Bar> ordered)
    {
        // An empty layout still draws a single empty track so the icon is never blank.
        if (ordered.Count == 0)
        {
            ordered = [new IconLayout.Bar(UsedPercent: null, Weight: 1.0, Provider: "None")];
        }

        var count = ordered.Count;
        var bars = new List<BarSpec>(count);

        var totalSeparator = 0;
        for (var i = 0; i < count - 1; i++)
        {
            totalSeparator += ordered[i].Provider != ordered[i + 1].Provider ? SepCrossProvider : SepSameProvider;
        }

        var available = ContentHeight - totalSeparator;
        var totalWeight = 0.0;
        foreach (var bar in ordered)
        {
            totalWeight += bar.Weight;
        }

        if (totalWeight <= 0)
        {
            totalWeight = count;
        }

        var y = ContentTop;
        for (var i = 0; i < count; i++)
        {
            var height = i == count - 1
                ? ContentBottom - y                                        // last bar absorbs rounding
                : (int)Math.Round(available * ordered[i].Weight / totalWeight);

            bars.Add(new BarSpec(y, height, ordered[i].UsedPercent));
            y += height;

            if (i < count - 1)
            {
                y += ordered[i].Provider != ordered[i + 1].Provider ? SepCrossProvider : SepSameProvider;
            }
        }

        return bars;
    }

    private static nint RenderIcon(List<BarSpec> bars)
    {
        var xor = new byte[IconSize * IconSize * 4]; // BGRA
        var and = new byte[IconSize * IconSize / 8];

        // Dark plate (gaps between bars read as this colour).
        for (var y = PlateInset; y < IconSize - PlateInset; y++)
        {
            for (var x = PlateInset; x < IconSize - PlateInset; x++)
            {
                PutPixel(xor, x, y, Plate.R, Plate.G, Plate.B);
            }
        }

        foreach (var bar in bars)
        {
            // Track (grey background of the bar).
            for (var y = bar.Y; y < bar.Y + bar.Height; y++)
            {
                for (var x = BarLeft; x < BarRight; x++)
                {
                    PutPixel(xor, x, y, Track.R, Track.G, Track.B);
                }
            }

            if (bar.UsedPercent is not { } percent)
            {
                continue;
            }

            var (r, g, b) = LevelColor(LevelFromPercent(percent));
            var clamped = Math.Clamp(percent, 0, 100);
            var fillEnd = (int)Math.Min(BarRight, BarLeft + Math.Round(BarWidth * clamped / 100.0));
            for (var y = bar.Y; y < bar.Y + bar.Height; y++)
            {
                for (var x = BarLeft; x < fillEnd; x++)
                {
                    PutPixel(xor, x, y, r, g, b);
                }
            }
        }

        var icon = NativeMethods.CreateIcon(0, IconSize, IconSize, 1, 32, and, xor);
        if (icon == 0)
        {
            throw new InvalidOperationException("Failed to create tray icon.");
        }

        return icon;
    }

    private static void PutPixel(byte[] xor, int x, int y, byte r, byte g, byte b)
    {
        var index = (y * IconSize + x) * 4;
        xor[index] = b;
        xor[index + 1] = g;
        xor[index + 2] = r;
        xor[index + 3] = 255;
    }
}
