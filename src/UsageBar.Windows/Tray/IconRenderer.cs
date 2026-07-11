using UsageBar.Core.Application;
using UsageBar.Core.Tray;

namespace UsageBar.Windows.Tray;

internal static class IconRenderer
{
    public static nint CreateUsageIcon(IReadOnlyList<IconLayout.Bar> bars)
    {
        var result = IconBitmapRenderer.Render(bars);
        var icon = NativeMethods.CreateIcon(0, result.Width, result.Height, 1, 32, result.And, result.Xor);
        if (icon == 0)
        {
            throw new InvalidOperationException("Failed to create tray icon.");
        }

        return icon;
    }
}