namespace UsageBar.Shell.Tray;

internal static class IconFactory
{
    private const int Size = 32;

    public static nint CreateUsageIcon(double? codexPrimaryUsedPercent)
    {
        var accent = GetAccent(codexPrimaryUsedPercent);
        var usedPercent = codexPrimaryUsedPercent is null ? 100 : Math.Clamp(codexPrimaryUsedPercent.Value, 0, 100);
        var filledWidth = Math.Max(2, (int)Math.Round(Size * usedPercent / 100d));

        var xor = new byte[Size * Size * 4];
        var and = new byte[Size * Size / 8];

        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var index = (y * Size + x) * 4;
                var border = x < 2 || y < 2 || x >= Size - 2 || y >= Size - 2;
                var fill = x < filledWidth;
                var color = border
                    ? (R: (byte)245, G: (byte)245, B: (byte)245)
                    : fill
                        ? accent
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
