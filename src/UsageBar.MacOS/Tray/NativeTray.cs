using AppKit;
using CoreGraphics;
using Foundation;
using UsageBar.Core.Application;
using UsageBar.Core.Domain;
using UsageBar.Core.Tray;

namespace UsageBar.MacOS.Tray;

internal sealed class NativeTray : INativeTray
{
    private readonly NSStatusItem _statusItem;

    public event Action? SettingsRequested;
    public event Action? RefreshRequested;
    public event Action? ExitRequested;

    public NativeTray()
    {
        _statusItem = NSStatusBar.SystemStatusBar.CreateStatusItem(NSStatusItemLength.Square);
        _statusItem.Button.Image = CreateEmptyIcon();

        var menu = new NSMenu();

        var settingsItem = new NSMenuItem("Settings", (_, _) => SettingsRequested?.Invoke());
        settingsItem.KeyEquivalent = ",";
        menu.AddItem(settingsItem);

        var refreshItem = new NSMenuItem("Refresh", (_, _) => RefreshRequested?.Invoke());
        refreshItem.KeyEquivalent = "r";
        menu.AddItem(refreshItem);

        menu.AddItem(NSMenuItem.SeparatorItem);

        var quitItem = new NSMenuItem("Quit", (_, _) => ExitRequested?.Invoke());
        quitItem.KeyEquivalent = "q";
        menu.AddItem(quitItem);

        _statusItem.Menu = menu;
    }

    public void UpdateIcon(NSImage image)
    {
        _statusItem.Button.Image = image;
    }

    public static NSImage RenderIcon(IReadOnlyList<IconLayout.Bar> bars)
    {
        var bitmap = IconBitmapRenderer.Render(bars);
        return CreateImageFromRgba(bitmap.Xor, bitmap.Width, bitmap.Height);
    }

    public void ShowNotification(NotificationLevel level, string message)
    {
        var notification = new NSUserNotification
        {
            Title = "Usage Bar",
            InformativeText = message,
            DeliveryDate = NSDate.Now,
        };

        NSUserNotificationCenter.DefaultUserNotificationCenter.DeliverNotification(notification);
    }

    private static NSImage CreateEmptyIcon()
    {
        var bars = new IconLayout.Bar[] { new(UsedPercent: null, Weight: 1.0, Provider: "None") };
        return RenderIcon(bars);
    }

    private static NSImage CreateImageFromRgba(byte[] rgba, int width, int height)
    {
        var bytesPerRow = width * 4;
        using var provider = new CGDataProvider(rgba, 0, rgba.Length);
        using var colorSpace = CGColorSpace.CreateDeviceRGB();

        using var cgImage = new CGImage(
            width, height,
            bitsPerComponent: 8, bitsPerPixel: 32, bytesPerRow: bytesPerRow,
            colorSpace,
            CGBitmapFlags.ByteOrder32Little | CGBitmapFlags.PremultipliedFirst,
            provider,
            decode: null, shouldInterpolate: false, intent: CGColorRenderingIntent.Default);

        return new NSImage(cgImage, CGSize.Empty);
    }
}
