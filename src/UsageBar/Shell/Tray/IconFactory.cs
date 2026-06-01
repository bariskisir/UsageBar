using UsageBar.Domain;

namespace UsageBar.Shell.Tray;

internal static class IconFactory
{
    private const int IconSize = 32;

    // CodexBar-style geometry: a 2px transparent margin around a dark plate,
    // with the bars inset inside it.
    private const int PlateInset = 2;
    private const int BarLeft = 4;
    private const int BarRight = IconSize - 4; // 28
    private const int BarWidth = BarRight - BarLeft; // 24
    private const int ContentTop = 6;
    private const int ContentBottom = IconSize - 6; // 26
    private const int ContentHeight = ContentBottom - ContentTop; // 20

    // Separator (gap) heights between stacked bars.
    private const int SepSameProvider = 1;
    private const int SepCrossProvider = 2;

    // CodexBar palette.
    private static readonly (byte R, byte G, byte B) Plate = (60, 60, 70);
    private static readonly (byte R, byte G, byte B) Track = (80, 80, 90);

    /// <summary>
    /// Usage status level → fill colour. Ported from CodexBar's <c>UsageLevel</c>.
    /// </summary>
    private enum UsageLevel
    {
        Low,      // < 50%
        Medium,   // < 80%
        High,     // < 95%
        Critical  // >= 95%
    }

    private static UsageLevel LevelFromPercent(double percent)
    {
        return percent switch
        {
            < 50.0 => UsageLevel.Low,
            < 80.0 => UsageLevel.Medium,
            < 95.0 => UsageLevel.High,
            _ => UsageLevel.Critical
        };
    }

    private static (byte R, byte G, byte B) LevelColor(UsageLevel level)
    {
        return level switch
        {
            UsageLevel.Low => (76, 175, 80),      // Green  #4CAF50
            UsageLevel.Medium => (255, 193, 7),    // Amber  #FFC107
            UsageLevel.High => (255, 152, 0),      // Orange #FF9800
            UsageLevel.Critical => (244, 67, 54),  // Red    #F44336
            _ => (76, 175, 80)
        };
    }

    /// <summary>
    /// Create a tray icon from provider usage windows and plans.
    /// </summary>
    public static nint CreateUsageIcon(
        IReadOnlyList<UsageBarWindow> windows,
        IReadOnlyList<(string Provider, string Plan)> plans)
    {
        var bars = BuildBarLayout(windows, plans);
        return RenderIcon(bars);
    }

    private readonly record struct BarSpec(int Y, int Height, double? UsedPercent);

    private static List<BarSpec> BuildBarLayout(
        IReadOnlyList<UsageBarWindow> windows,
        IReadOnlyList<(string Provider, string Plan)> plans)
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

        var codex5h = FindWindow(codexWindows, "Session");
        var codex7d = FindWindow(codexWindows, "Weekly");
        var claude5h = FindWindow(claudeWindows, "Session");
        var claude7d = FindWindow(claudeWindows, "Weekly");

        var hasCodex = codexWindows.Count > 0;
        var hasClaude = claudeWindows.Count > 0;
        var codexPlanIsFree = ProviderPlanIs(plans, "Codex", "Free");
        var codexFreeWindow = codexPlanIsFree
            ? codex7d ?? codex5h
            : codex7d;
        var codexIsFree = codexFreeWindow is not null && (codexPlanIsFree || codex5h is null);
        var codexIsPro = codex5h is not null && !codexPlanIsFree;
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
            ordered.Add((codexFreeWindow!.UsedPercent, "Codex"));
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
            ordered.Add((codexFreeWindow!.UsedPercent, "Codex"));
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

        // Fallback: empty bar.
        if (ordered.Count == 0)
            ordered.Add((null, "None"));

        return AssignBarPositions(ordered);
    }

    private static bool ProviderPlanIs(
        IReadOnlyList<(string Provider, string Plan)> plans,
        string providerName,
        string expected)
    {
        foreach (var (provider, plan) in plans)
        {
            if (string.Equals(provider, providerName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(plan, expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
    /// Assign pixel positions to ordered bars with proportional heights.
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
        var ratios = GetHeightRatios(n);
        var totalRatio = ratios.Sum();

        var y = ContentTop;
        for (var i = 0; i < n; i++)
        {
            var barHeight = (int)Math.Round(available * ratios[i] / totalRatio);
            // Give any rounding remainder to the last bar.
            if (i == n - 1)
                barHeight = ContentBottom - y;

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

    private static double[] GetHeightRatios(int n)
    {
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
        var xor = new byte[IconSize * IconSize * 4]; // ARGB
        var and = new byte[IconSize * IconSize / 8];

        // Dark plate (fills the inset area; gaps between bars read as this colour).
        for (var y = PlateInset; y < IconSize - PlateInset; y++)
        {
            for (var x = PlateInset; x < IconSize - PlateInset; x++)
            {
                PutPixelXor(xor, x, y, Plate.R, Plate.G, Plate.B, 255);
            }
        }

        foreach (var bar in bars)
        {
            // Track (grey background of the bar).
            for (var y = bar.Y; y < bar.Y + bar.Height; y++)
            {
                for (var x = BarLeft; x < BarRight; x++)
                {
                    PutPixelXor(xor, x, y, Track.R, Track.G, Track.B, 255);
                }
            }

            // Fill — only when the window has a known percentage.
            if (bar.UsedPercent is { } pct)
            {
                var (r, g, b) = LevelColor(LevelFromPercent(pct));
                var clamped = Math.Clamp(pct, 0, 100);
                var fillEnd = (int)Math.Min(BarRight, BarLeft + Math.Round(BarWidth * clamped / 100.0));
                for (var y = bar.Y; y < bar.Y + bar.Height; y++)
                {
                    for (var x = BarLeft; x < fillEnd; x++)
                    {
                        PutPixelXor(xor, x, y, r, g, b, 255);
                    }
                }
            }
        }

        var icon = NativeMethods.CreateIcon(0, IconSize, IconSize, 1, 32, and, xor);
        if (icon == 0)
            throw new InvalidOperationException("Failed to create tray icon.");

        return icon;
    }

    /// <summary>Writes one pixel into the BGRA XOR buffer.</summary>
    private static void PutPixelXor(byte[] xor, int x, int y, byte r, byte g, byte b, byte a)
    {
        var index = (y * IconSize + x) * 4;
        xor[index] = b;
        xor[index + 1] = g;
        xor[index + 2] = r;
        xor[index + 3] = a;
    }
}
