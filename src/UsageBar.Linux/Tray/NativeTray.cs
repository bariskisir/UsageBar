using Tmds.DBus;
using UsageBar.Core.Application;
using UsageBar.Core.Domain;
using UsageBar.Core.Tray;

namespace UsageBar.Linux.Tray;

[DBusInterface("org.kde.StatusNotifierItem")]
internal interface IStatusNotifierItem : IDBusObject
{
    Task ActivateAsync(int x, int y);
    Task SecondaryActivateAsync(int x, int y);
    Task ContextMenuAsync(int x, int y);
    Task ScrollAsync(int delta, string orientation);
    Task<object> GetAsync(string prop);
    Task<IDictionary<string, object>> GetAllAsync();
    Task SetAsync(string prop, object val);
    Task<IDisposable> WatchPropertiesAsync(Action<(string, IDictionary<string, object>, string[])> handler);
}

internal sealed class StatusNotifierItem : IStatusNotifierItem
{
    private readonly NativeTray _tray;
    private readonly IDictionary<string, object> _props;
    private Action<(string, IDictionary<string, object>, string[])>? _watchHandler;

    public StatusNotifierItem(NativeTray tray)
    {
        _tray = tray;
        _props = new Dictionary<string, object>
        {
            ["Category"] = "ApplicationStatus",
            ["Id"] = "usagebar",
            ["Title"] = "Usage Bar",
            ["Status"] = "Active",
            ["WindowId"] = 0,
            ["ItemIsMenu"] = false,
        };
        ObjectPath = new ObjectPath("/StatusNotifierItem");
    }

    public ObjectPath ObjectPath { get; }

    public Task ActivateAsync(int x, int y)
    {
        _tray.ToggleTooltip();
        return Task.CompletedTask;
    }

    public Task SecondaryActivateAsync(int x, int y)
    {
        _tray.RaiseRefreshRequested();
        return Task.CompletedTask;
    }

    public Task ContextMenuAsync(int x, int y)
    {
        _tray.RaiseSettingsRequested();
        return Task.CompletedTask;
    }

    public Task ScrollAsync(int delta, string orientation) => Task.CompletedTask;

    public Task<object> GetAsync(string prop) =>
        Task.FromResult(_props.TryGetValue(prop, out var value) ? value : new object());

    public Task<IDictionary<string, object>> GetAllAsync() =>
        Task.FromResult(_props);

    public Task SetAsync(string prop, object val) => Task.CompletedTask;

    public Task<IDisposable> WatchPropertiesAsync(Action<(string, IDictionary<string, object>, string[])> handler)
    {
        _watchHandler = handler;
        return Task.FromResult<IDisposable>(new DisposableAction(() => _watchHandler = null));
    }

    public void UpdateIconPixmap((int width, int height, byte[] data)[] pixmap)
    {
        _props["IconPixmap"] = pixmap;
        _watchHandler?.Invoke(("org.kde.StatusNotifierItem", new Dictionary<string, object> { ["IconPixmap"] = pixmap }, []));
    }

    public void UpdateToolTip((string icon, string title, string description) tooltip)
    {
        _props["ToolTip"] = tooltip;
        _watchHandler?.Invoke(("org.kde.StatusNotifierItem", new Dictionary<string, object> { ["ToolTip"] = tooltip }, []));
    }

    private sealed class DisposableAction(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}

internal interface INativeTray
{
    event Action? SettingsRequested;
    event Action? RefreshRequested;
    event Action? ExitRequested;
    event Action? TooltipToggleRequested;

    void UpdateIcon(IReadOnlyList<IconLayout.Bar> bars);
    void ShowNotification(NotificationLevel level, string message);
}

internal sealed class NativeTray : INativeTray, IDisposable
{
    private readonly Connection _connection;
    private readonly StatusNotifierItem _item;

    public event Action? SettingsRequested;
    public event Action? RefreshRequested;
    public event Action? ExitRequested;
    public event Action? TooltipToggleRequested;

    public NativeTray()
    {
        _connection = Connection.Session ?? throw new InvalidOperationException("D-Bus session bus is not available.");

        _item = new StatusNotifierItem(this);

        var serviceName = $"org.kde.StatusNotifierItem-{Environment.ProcessId}-1";

        _connection.RegisterServiceAsync(serviceName).GetAwaiter().GetResult();
        _connection.RegisterObjectAsync(_item).GetAwaiter().GetResult();

        RegisterWithWatcher(serviceName);

        var bars = new IconLayout.Bar[] { new(UsedPercent: null, Weight: 1.0, Provider: "None") };
        UpdateIcon(bars);
    }

    public void UpdateIcon(IReadOnlyList<IconLayout.Bar> bars)
    {
        var bitmap = IconBitmapRenderer.Render(bars);
        var pixmap = CreateIconPixmap(bitmap.Xor, bitmap.Width, bitmap.Height);
        _item.UpdateIconPixmap(pixmap);
    }

    public void ShowNotification(NotificationLevel level, string message)
    {
        try
        {
            var proxy = _connection.CreateProxy<INotifications>(
                "org.freedesktop.Notifications",
                new ObjectPath("/org/freedesktop/Notifications"));

            proxy.NotifyAsync(
                "Usage Bar", 0, string.Empty,
                GetLevelTitle(level), message,
                [], new Dictionary<string, object>(), 5000);
        }
        catch
        {
        }
    }

    internal void RaiseSettingsRequested() => SettingsRequested?.Invoke();
    internal void RaiseRefreshRequested() => RefreshRequested?.Invoke();
    internal void RaiseExitRequested() => ExitRequested?.Invoke();
    internal void ToggleTooltip() => TooltipToggleRequested?.Invoke();

    public void Dispose()
    {
        _connection.Dispose();
    }

    private void RegisterWithWatcher(string serviceName)
    {
        try
        {
            var proxy = _connection.CreateProxy<IStatusNotifierWatcher>(
                "org.kde.StatusNotifierWatcher",
                new ObjectPath("/StatusNotifierWatcher"));

            proxy.RegisterStatusNotifierItemAsync(serviceName);
        }
        catch
        {
        }
    }

    private static (int, int, byte[])[] CreateIconPixmap(byte[] rgba, int width, int height)
    {
        var argb = new byte[rgba.Length];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            argb[i] = rgba[i + 3];
            argb[i + 1] = rgba[i + 2];
            argb[i + 2] = rgba[i + 1];
            argb[i + 3] = rgba[i];
        }

        return [(width, height, argb)];
    }

    private static string GetLevelTitle(NotificationLevel level) => level switch
    {
        NotificationLevel.High => "High Usage",
        NotificationLevel.Critical => "Critical Usage",
        NotificationLevel.Reset => "Usage Reset",
        _ => "Usage Bar",
    };
}

[DBusInterface("org.kde.StatusNotifierWatcher")]
internal interface IStatusNotifierWatcher : IDBusObject
{
    Task RegisterStatusNotifierItemAsync(string service);
}

[DBusInterface("org.freedesktop.Notifications")]
internal interface INotifications : IDBusObject
{
    Task<uint> NotifyAsync(string appName, uint replacesId, string appIcon,
        string summary, string body, string[] actions,
        IDictionary<string, object> hints, int expireTimeout);
}