using UsageBar.Core.Application;

namespace UsageBar.Core.Tray;

internal readonly record struct IconBitmapData(byte[] Xor, byte[] And, int Width, int Height);

internal static class IconBitmapRenderer
{
    public const int IconSize = 32;

    private const int PlateInset = 2;
    private const int BarLeft = 4;
    private const int BarRight = IconSize - 4;
    private const int BarWidth = BarRight - BarLeft;
    private const int ContentTop = 6;
    private const int ContentBottom = IconSize - 6;
    private const int ContentHeight = ContentBottom - ContentTop;

    private const int SepSameProvider = 1;
    private const int SepCrossProvider = 2;

    private static readonly (byte R, byte G, byte B) Plate = (60, 60, 70);
    private static readonly (byte R, byte G, byte B) Track = (80, 80, 90);

    private enum UsageLevel
    {
        Low,
        Medium,
        High,
        Critical,
    }

    private readonly record struct BarSpec(int Y, int Height, double? UsedPercent);

    public static IconBitmapData Render(IReadOnlyList<IconLayout.Bar> bars)
    {
        var barSpecs = AssignBarPositions(bars);
        var xor = new byte[IconSize * IconSize * 4];
        var and = new byte[IconSize * IconSize / 8];

        for (var y = PlateInset; y < IconSize - PlateInset; y++)
        {
            for (var x = PlateInset; x < IconSize - PlateInset; x++)
            {
                PutPixel(xor, x, y, Plate.R, Plate.G, Plate.B);
            }
        }

        for (var i = 0; i < barSpecs.Count; i++)
        {
            var bar = barSpecs[i];
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

        return new IconBitmapData(xor, and, IconSize, IconSize);
    }

    private static UsageLevel LevelFromPercent(double percent) => percent switch
    {
        < 50.0 => UsageLevel.Low,
        < 80.0 => UsageLevel.Medium,
        < 95.0 => UsageLevel.High,
        _ => UsageLevel.Critical,
    };

    private static (byte R, byte G, byte B) LevelColor(UsageLevel level) => level switch
    {
        UsageLevel.Low => (76, 175, 80),
        UsageLevel.Medium => (255, 193, 7),
        UsageLevel.High => (255, 152, 0),
        UsageLevel.Critical => (244, 67, 54),
        _ => (76, 175, 80),
    };

    private static List<BarSpec> AssignBarPositions(IReadOnlyList<IconLayout.Bar> ordered)
    {
        if (ordered.Count == 0)
        {
            ordered = [new IconLayout.Bar(UsedPercent: null, Weight: 1.0, Provider: "None")];
        }

        var count = ordered.Count;
        var bars = new List<BarSpec>(Math.Min(count, ContentHeight));

        var totalSeparator = 0;
        for (var i = 0; i < count - 1; i++)
        {
            totalSeparator += ordered[i].Provider != ordered[i + 1].Provider ? SepCrossProvider : SepSameProvider;
        }

        var available = Math.Max(1, ContentHeight - totalSeparator);
        var totalWeight = 0.0;
        for (var i = 0; i < count; i++)
        {
            totalWeight += ordered[i].Weight;
        }

        if (totalWeight <= 0)
        {
            totalWeight = count;
        }

        var y = ContentTop;
        for (var i = 0; i < count; i++)
        {
            if (y >= ContentBottom)
            {
                break;
            }

            var isLast = i == count - 1;
            var height = isLast
                ? Math.Max(1, ContentBottom - y)
                : Math.Max(1, (int)Math.Round(available * ordered[i].Weight / totalWeight));

            height = Math.Min(height, Math.Max(1, ContentBottom - y));

            bars.Add(new BarSpec(y, height, ordered[i].UsedPercent));
            y += height;

            if (i < count - 1 && ordered[i].Provider != ordered[i + 1].Provider)
            {
                y += SepCrossProvider;
            }
            else if (i < count - 1)
            {
                y += SepSameProvider;
            }
        }

        if (bars.Count > 0)
        {
            var last = bars[^1];
            bars[^1] = last with { Height = Math.Max(1, ContentBottom - last.Y) };
        }

        return bars;
    }

    private static void PutPixel(byte[] xor, int x, int y, byte r, byte g, byte b)
    {
        if (x < 0 || x >= IconSize || y < 0 || y >= IconSize)
        {
            return;
        }

        var index = (y * IconSize + x) * 4;
        xor[index] = b;
        xor[index + 1] = g;
        xor[index + 2] = r;
        xor[index + 3] = 255;
    }
}