using UsageBar.Domain;

namespace UsageBar.Shell.Tray;

internal static class IconFactory
{
    private const int Size = 32;
    private const int BorderWidth = 2;
    private const int ContentWidth = Size - (BorderWidth * 2);
    private const int ContentTop = BorderWidth;
    private const int ContentBottom = Size - BorderWidth;
    private const int ContentHeight = ContentBottom - ContentTop;

    // Separator widths in pixels.
    private const int SepSameProvider = 1;
    private const int SepCrossProvider = 2;

    /// <summary>
    /// Create a tray icon from provider usage windows.
    /// Layout is determined by which windows are present (see BuildBarLayout).
    /// </summary>
    public static nint CreateUsageIcon(IReadOnlyList<UsageBarWindow> windows)
    {
        var bars = BuildBarLayout(windows);
        return RenderIcon(bars);
    }

    private readonly record struct BarSpec(int Y, int Height, double? UsedPercent);

    private static List<BarSpec> BuildBarLayout(IReadOnlyList<UsageBarWindow> windows)
    {
        var codexWindows = new List<UsageBarWindow>();
        var claudeWindows = new List<UsageBarWindow>();

        foreach (var w in windows)
        {
            if (w.ProviderName == "Codex")
                codexWindows.Add(w);
            else if (w.ProviderName == "Claude")
                claudeWindows.Add(w);
        }

        var codex5h = FindWindow(codexWindows, "5h");
        var codex7d = FindWindow(codexWindows, "7d");
        var claude5h = FindWindow(claudeWindows, "5h");
        var claude7d = FindWindow(claudeWindows, "7d");

        var hasCodex = codexWindows.Count > 0;
        var hasClaude = claudeWindows.Count > 0;
        var codexIsFree = codex5h is null && codex7d is not null;
        var codexIsPro = codex5h is not null;
        var claudeIsSubscriber = claude5h is not null && claude7d is not null;

        // Build ordered window list with provider tags for separator logic.
        var ordered = new List<(double? UsedPercent, string Provider)>();

        if (codexIsPro && hasClaude && claudeIsSubscriber)
        {
            // Case 5: Codex 5h+7d + Claude 5h+7d → 25-25-25-25
            ordered.Add((codex5h!.UsedPercent, "Codex"));
            ordered.Add((codex7d!.UsedPercent, "Codex"));
            ordered.Add((claude5h!.UsedPercent, "Claude"));
            ordered.Add((claude7d!.UsedPercent, "Claude"));
        }
        else if (codexIsFree && hasClaude && claudeIsSubscriber)
        {
            // Case 4: Codex free (7d only) + Claude 5h+7d → 50-25-25
            ordered.Add((codex7d!.UsedPercent, "Codex"));
            ordered.Add((claude5h!.UsedPercent, "Claude"));
            ordered.Add((claude7d!.UsedPercent, "Claude"));
        }
        else if (!hasCodex && claudeIsSubscriber)
        {
            // Case 3: Claude only 5h+7d → 50-50
            ordered.Add((claude5h!.UsedPercent, "Claude"));
            ordered.Add((claude7d!.UsedPercent, "Claude"));
        }
        else if (codexIsPro && !hasClaude)
        {
            // Case 2: Codex pro 5h+7d only → 50-50
            ordered.Add((codex5h!.UsedPercent, "Codex"));
            if (codex7d is not null)
                ordered.Add((codex7d.UsedPercent, "Codex"));
        }
        else if (codexIsFree && !hasClaude)
        {
            // Case 1: Codex free (7d only) → full bar
            ordered.Add((codex7d!.UsedPercent, "Codex"));
        }
        else if (hasCodex && hasClaude)
        {
            // Fallback mixed: show whatever we have in a reasonable order.
            if (codex5h is not null) ordered.Add((codex5h.UsedPercent, "Codex"));
            if (codex7d is not null) ordered.Add((codex7d.UsedPercent, "Codex"));
            if (claude5h is not null) ordered.Add((claude5h.UsedPercent, "Claude"));
            if (claude7d is not null) ordered.Add((claude7d.UsedPercent, "Claude"));
        }
        else if (hasClaude)
        {
            // Claude only, not subscriber (single window or mismatched).
            if (claude5h is not null) ordered.Add((claude5h.UsedPercent, "Claude"));
            if (claude7d is not null) ordered.Add((claude7d.UsedPercent, "Claude"));
        }

        // Fallback: empty gray bar.
        if (ordered.Count == 0)
            ordered.Add((null, "None"));

        return AssignBarPositions(ordered);
    }

    private static UsageBarWindow? FindWindow(List<UsageBarWindow> windows, string label)
    {
        foreach (var w in windows)
        {
            if (w.WindowLabel == label)
                return w;
        }
        return null;
    }

    /// <summary>
    /// Assign pixel positions to ordered bars. Separators are 2 px between
    /// different providers and 1 px within the same provider. The remaining
    /// space is divided according to the case-dependent ratio.
    /// </summary>
    private static List<BarSpec> AssignBarPositions(
        List<(double? UsedPercent, string Provider)> ordered)
    {
        var n = ordered.Count;
        var bars = new List<BarSpec>(n);

        // Calculate total separator height.
        var totalSep = 0;
        for (var i = 0; i < n - 1; i++)
        {
            totalSep += ordered[i].Provider != ordered[i + 1].Provider
                ? SepCrossProvider
                : SepSameProvider;
        }

        var available = ContentHeight - totalSep;

        // Determine height ratio for each bar based on the case.
        var ratios = GetHeightRatios(ordered);
        var totalRatio = ratios.Sum();

        var y = ContentTop;
        for (var i = 0; i < n; i++)
        {
            var barHeight = (int)Math.Round(available * ratios[i] / totalRatio);
            // Give any rounding remainder to the last bar.
            if (i == n - 1)
                barHeight = (ContentTop + ContentHeight) - y; // fill to bottom

            bars.Add(new BarSpec(y, barHeight, ordered[i].UsedPercent));
            y += barHeight;

            // Add separator after this bar (except after the last).
            if (i < n - 1)
            {
                y += ordered[i].Provider != ordered[i + 1].Provider
                    ? SepCrossProvider
                    : SepSameProvider;
            }
        }

        return bars;
    }

    /// <summary>
    /// Return the relative height ratios for each bar position.
    /// The caller divides each by the sum to get the fraction of available space.
    /// </summary>
    private static double[] GetHeightRatios(
        List<(double? UsedPercent, string Provider)> ordered)
    {
        var n = ordered.Count;

        return n switch
        {
            1 => [1.0],
            2 => [1.0, 1.0],                                       // 50-50
            3 => [2.0, 1.0, 1.0],                                   // 50-25-25
            4 => [1.0, 1.0, 1.0, 1.0],                              // 25-25-25-25
            _ => Enumerable.Repeat(1.0, n).ToArray()                // equal split fallback
        };
    }

    private static nint RenderIcon(List<BarSpec> bars)
    {
        var hasAnyUsage = false;
        foreach (var bar in bars)
        {
            if (bar.UsedPercent is not null)
            {
                hasAnyUsage = true;
                break;
            }
        }

        var xor = new byte[Size * Size * 4];
        var and = new byte[Size * Size / 8];

        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var index = (y * Size + x) * 4;
                var isBorder = x < BorderWidth || y < BorderWidth ||
                               x >= Size - BorderWidth || y >= Size - BorderWidth;

                if (isBorder)
                {
                    xor[index] = 245;
                    xor[index + 1] = 245;
                    xor[index + 2] = 245;
                    xor[index + 3] = 255;
                    continue;
                }

                // Separator pixel?
                if (IsSeparatorPixel(y, bars))
                {
                    xor[index] = 245;
                    xor[index + 1] = 245;
                    xor[index + 2] = 245;
                    xor[index + 3] = 255;
                    continue;
                }

                var bar = FindBar(y, bars);
                if (bar is null)
                {
                    // Background.
                    xor[index] = 32;
                    xor[index + 1] = 36;
                    xor[index + 2] = 41;
                    xor[index + 3] = 255;
                    continue;
                }

                var (r, g, b) = GetAccent(bar.Value.UsedPercent);
                var filledWidth = GetFilledWidth(bar.Value.UsedPercent, !hasAnyUsage);
                var isFilled = x < filledWidth;

                xor[index] = isFilled ? b : (byte)32;
                xor[index + 1] = isFilled ? g : (byte)36;
                xor[index + 2] = isFilled ? r : (byte)41;
                xor[index + 3] = 255;
            }
        }

        var icon = NativeMethods.CreateIcon(0, Size, Size, 1, 32, and, xor);
        if (icon == 0)
            throw new InvalidOperationException("Failed to create tray icon.");

        return icon;
    }

    private static bool IsSeparatorPixel(int y, List<BarSpec> bars)
    {
        for (var i = 0; i < bars.Count - 1; i++)
        {
            var barBottom = bars[i].Y + bars[i].Height;
            var nextBarTop = bars[i + 1].Y;
            if (y >= barBottom && y < nextBarTop)
                return true;
        }
        return false;
    }

    private static BarSpec? FindBar(int y, List<BarSpec> bars)
    {
        foreach (var bar in bars)
        {
            if (y >= bar.Y && y < bar.Y + bar.Height)
                return bar;
        }
        return null;
    }

    private static int GetFilledWidth(double? usedPercent, bool fillWhenUnknown)
    {
        if (usedPercent is null)
            return fillWhenUnknown ? Size - BorderWidth : BorderWidth;

        var clamped = Math.Clamp(usedPercent.Value, 0, 100);
        return BorderWidth + (int)Math.Round(ContentWidth * clamped / 100d);
    }

    // --- Dynamic color ---
    //   0% → green  (0,   255, 0)
    //  50% → yellow (255, 255, 0)
    // 100% → red    (255, 0,   0)
    // 0-50: green→yellow (R ramps up), 50-100: yellow→red (G ramps down).
    private static readonly (byte R, byte G, byte B) Gray = (140, 145, 152);

    private static (byte R, byte G, byte B) GetAccent(double? usedPercent)
    {
        if (usedPercent is null)
            return Gray;

        var pct = Math.Clamp(usedPercent.Value, 0, 100);

        if (pct <= 50.0)
        {
            var t = pct / 50.0;                       // 0 → 1
            return ((byte)Math.Round(255.0 * t),      // R: 0 → 255
                    (byte)255,                          // G: fixed 255
                    (byte)0);                           // B: fixed 0
        }
        else
        {
            var t = (pct - 50.0) / 50.0;               // 0 → 1
            return ((byte)255,                          // R: fixed 255
                    (byte)Math.Round(255.0 * (1.0 - t)), // G: 255 → 0
                    (byte)0);                           // B: fixed 0
        }
    }
}
