using System.Runtime.InteropServices;
using Gtk;
using UsageBar.Core.Application;
using UsageBar.Core.Tray;
using UsageBar.Linux.Infrastructure;

namespace UsageBar.Linux.Tray;

internal sealed class FallbackStatusWindow : IDisposable
{
    private readonly GtkDispatcher _dispatcher;
    private readonly Window _window;
    private readonly Image _icon;
    private Gdk.Pixbuf? _pixbuf;

    public FallbackStatusWindow(GtkDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _window = new Window("Usage Bar");
        _window.SetDefaultSize(360, 116);
        _window.Resizable = false;
        _window.DeleteEvent += (_, args) =>
        {
            args.RetVal = true;
            ExitRequested?.Invoke();
        };

        var root = new Box(Orientation.Vertical, 10)
        {
            BorderWidth = 14,
        };
        var header = new Box(Orientation.Horizontal, 12);
        _icon = new Image();
        _icon.SetSizeRequest(40, 40);
        header.PackStart(_icon, expand: false, fill: false, padding: 0);

        var description = new Label
        {
            Xalign = 0,
            Text = GetMissingHostMessage(),
        };
        header.PackStart(description, expand: true, fill: true, padding: 0);
        root.PackStart(header, expand: true, fill: true, padding: 0);

        var actions = new ButtonBox(Orientation.Horizontal)
        {
            Layout = ButtonBoxStyle.End,
            Spacing = 8,
        };
        var usageButton = new Button("Usage");
        usageButton.Clicked += (_, _) => UsageRequested?.Invoke();
        actions.Add(usageButton);
        var refreshButton = new Button("Refresh");
        refreshButton.Clicked += (_, _) => RefreshRequested?.Invoke();
        actions.Add(refreshButton);
        var settingsButton = new Button("Settings");
        settingsButton.Clicked += (_, _) => SettingsRequested?.Invoke();
        actions.Add(settingsButton);
        var quitButton = new Button("Quit");
        quitButton.Clicked += (_, _) => ExitRequested?.Invoke();
        actions.Add(quitButton);
        root.PackStart(actions, expand: false, fill: true, padding: 0);

        _window.Add(root);
        UpdateIcon([new IconLayout.Bar(UsedPercent: null, Weight: 1.0, Provider: "None")]);
    }

    public event System.Action? UsageRequested;
    public event System.Action? SettingsRequested;
    public event System.Action? RefreshRequested;
    public event System.Action? ExitRequested;

    internal Window Window => _window;

    public void Show()
    {
        _dispatcher.Invoke(() =>
        {
            _window.ShowAll();
            _window.Present();
        });
    }

    public void UpdateIcon(IReadOnlyList<IconLayout.Bar> bars)
    {
        var bitmap = IconBitmapRenderer.Render(bars);
        var rgba = ConvertBgraToRgba(bitmap.Xor);

        _dispatcher.Invoke(() =>
        {
            var pixbuf = new Gdk.Pixbuf(
                Gdk.Colorspace.Rgb,
                true,
                8,
                bitmap.Width,
                bitmap.Height);
            Marshal.Copy(rgba, 0, pixbuf.Pixels, rgba.Length);

            _icon.Pixbuf = pixbuf;
            _pixbuf?.Dispose();
            _pixbuf = pixbuf;
        });
    }

    public void Dispose()
    {
        _pixbuf?.Dispose();
        _window.Dispose();
    }

    private static byte[] ConvertBgraToRgba(byte[] bgra)
    {
        var rgba = new byte[bgra.Length];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            rgba[i] = bgra[i + 2];
            rgba[i + 1] = bgra[i + 1];
            rgba[i + 2] = bgra[i];
            rgba[i + 3] = bgra[i + 3];
        }

        return rgba;
    }

    private static string GetMissingHostMessage()
    {
        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
        return desktop?.Contains("GNOME", StringComparison.OrdinalIgnoreCase) == true
            ? "Usage Bar is running\nEnable GNOME AppIndicator support for a tray icon."
            : "Usage Bar is running\nNo system-tray host was detected.";
    }
}
