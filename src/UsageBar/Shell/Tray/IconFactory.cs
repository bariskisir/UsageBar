namespace UsageBar.Shell.Tray;

internal static class IconFactory
{
    private const int Size = 32;
    private const int BorderWidth = 2;
    private const int SeparatorTop = 15;
    private const int SeparatorBottom = 17;
    private const int ContentWidth = Size - (BorderWidth * 2);

    public static nint CreateUsageIcon(double? codexPrimaryUsedPercent, double? codexSecondaryUsedPercent)
    {
        var hasAnyUsage = codexPrimaryUsedPercent is not null || codexSecondaryUsedPercent is not null;
        var primaryAccent = GetAccent(codexPrimaryUsedPercent);
        var secondaryAccent = GetAccent(codexSecondaryUsedPercent);
        var primaryFilledWidth = GetFilledWidth(codexPrimaryUsedPercent, fillWhenUnknown: !hasAnyUsage);
        var secondaryFilledWidth = GetFilledWidth(codexSecondaryUsedPercent, fillWhenUnknown: !hasAnyUsage);

        var xor = new byte[Size * Size * 4];
        var and = new byte[Size * Size / 8];

        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var index = (y * Size + x) * 4;
                var border = x < BorderWidth || y < BorderWidth || x >= Size - BorderWidth || y >= Size - BorderWidth;
                var separator = y >= SeparatorTop && y < SeparatorBottom;
                var topFill = y < SeparatorTop && x < primaryFilledWidth;
                var bottomFill = y >= SeparatorBottom && x < secondaryFilledWidth;
                var color = border || separator
                    ? (R: (byte)245, G: (byte)245, B: (byte)245)
                    : topFill
                        ? primaryAccent
                        : bottomFill
                            ? secondaryAccent
                            : (R: (byte)32, G: (byte)36, B: (byte)41);

                xor[index] = color.B;
                xor[index + 1] = color.G;
                xor[index + 2] = color.R;
                xor[index + 3] = 255;
            }
        }

        var icon = NativeMethods.CreateIcon(0, Size, Size, 1, 32, and, xor);
        if (icon == 0)
        {
            throw new InvalidOperationException("Failed to create tray icon.");
        }

        return icon;
    }

    private static int GetFilledWidth(double? usedPercent, bool fillWhenUnknown)
    {
        if (usedPercent is null)
        {
            return fillWhenUnknown ? Size - BorderWidth : BorderWidth;
        }

        var clamped = Math.Clamp(usedPercent.Value, 0, 100);
        return BorderWidth + (int)Math.Round(ContentWidth * clamped / 100d);
    }

    private static (byte R, byte G, byte B) GetAccent(double? codexPrimaryUsedPercent)
    {
        if (codexPrimaryUsedPercent is null)
        {
            return (140, 145, 152);
        }

        var value = Math.Clamp(codexPrimaryUsedPercent.Value, 0, 100);

        return value switch
        {
            <= 30 => (35, 170, 88),
            <= 70 => (230, 190, 58),
            < 100 => (236, 126, 42),
            _ => (218, 55, 55)
        };
    }
}
