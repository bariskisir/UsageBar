using Microsoft.Extensions.Logging;
using Tmds.DBus;
using UsageBar.Core.Application;
using UsageBar.Core.Domain;
using UsageBar.Core.Tray;

namespace UsageBar.Linux.Tray;

[DBusInterface("org.kde.StatusNotifierItem", PropertyType = typeof(StatusNotifierItemProperties))]
public interface IStatusNotifierItem : IDBusObject
{
    Task ActivateAsync(int x, int y);
    Task SecondaryActivateAsync(int x, int y);
    Task ContextMenuAsync(int x, int y);
    Task ScrollAsync(int delta, string orientation);
    Task<object> GetAsync(string prop);
    Task<StatusNotifierItemProperties> GetAllAsync();
    Task SetAsync(string prop, object val);
    Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler);
    Task<IDisposable> WatchNewIconAsync(Action handler);
    Task<IDisposable> WatchNewToolTipAsync(Action handler);
}

[Dictionary]
public sealed class StatusNotifierItemProperties
{
    [Property(Access = PropertyAccess.Read)]
    public string Category = "ApplicationStatus";

    [Property(Access = PropertyAccess.Read)]
    public string Id = "usagebar";

    [Property(Access = PropertyAccess.Read)]
    public string Title = "Usage Bar";

    [Property(Access = PropertyAccess.Read)]
    public string Status = "Active";

    [Property(Access = PropertyAccess.Read)]
    public uint WindowId;

    [Property(Access = PropertyAccess.Read)]
    public bool ItemIsMenu;

    [Property(Access = PropertyAccess.Read)]
    public string IconThemePath = string.Empty;

    [Property(Access = PropertyAccess.Read)]
    public ObjectPath Menu = new("/MenuBar");

    [Property(Access = PropertyAccess.Read)]
    public string IconName = string.Empty;

    [Property(Access = PropertyAccess.Read)]
    public (int width, int height, byte[] data)[] IconPixmap = [];

    [Property(Access = PropertyAccess.Read)]
    public string OverlayIconName = string.Empty;

    [Property(Access = PropertyAccess.Read)]
    public (int width, int height, byte[] data)[] OverlayIconPixmap = [];

    [Property(Access = PropertyAccess.Read)]
    public string AttentionIconName = string.Empty;

    [Property(Access = PropertyAccess.Read)]
    public (int width, int height, byte[] data)[] AttentionIconPixmap = [];

    [Property(Access = PropertyAccess.Read)]
    public string AttentionMovieName = string.Empty;

    [Property(Access = PropertyAccess.Read)]
    public (string icon, (int width, int height, byte[] data)[] pixmap, string title, string description) ToolTip
        = (string.Empty, [], "Usage Bar", string.Empty);
}

internal sealed class StatusNotifierItem : IStatusNotifierItem
{
    private readonly NativeTray _tray;
    private readonly StatusNotifierItemProperties _props = new();
    private Action<PropertyChanges>? _watchHandler;
    private Action? _newIconHandler;
    private Action? _newToolTipHandler;

    public StatusNotifierItem(NativeTray tray)
    {
        _tray = tray;
        ObjectPath = new ObjectPath("/StatusNotifierItem");
    }

    public ObjectPath ObjectPath { get; }

    public Task ActivateAsync(int x, int y)
    {
        _tray.ToggleTooltip(x, y);
        return Task.CompletedTask;
    }

    public Task SecondaryActivateAsync(int x, int y)
    {
        _tray.ToggleTooltip(x, y);
        return Task.CompletedTask;
    }

    public Task ContextMenuAsync(int x, int y)
    {
        return Task.CompletedTask;
    }

    public Task ScrollAsync(int delta, string orientation) => Task.CompletedTask;

    public Task<object> GetAsync(string prop) => Task.FromResult(prop switch
    {
        nameof(StatusNotifierItemProperties.Category) => (object)_props.Category,
        nameof(StatusNotifierItemProperties.Id) => _props.Id,
        nameof(StatusNotifierItemProperties.Title) => _props.Title,
        nameof(StatusNotifierItemProperties.Status) => _props.Status,
        nameof(StatusNotifierItemProperties.WindowId) => _props.WindowId,
        nameof(StatusNotifierItemProperties.ItemIsMenu) => _props.ItemIsMenu,
        nameof(StatusNotifierItemProperties.IconThemePath) => _props.IconThemePath,
        nameof(StatusNotifierItemProperties.Menu) => _props.Menu,
        nameof(StatusNotifierItemProperties.IconName) => _props.IconName,
        nameof(StatusNotifierItemProperties.IconPixmap) => _props.IconPixmap,
        nameof(StatusNotifierItemProperties.OverlayIconName) => _props.OverlayIconName,
        nameof(StatusNotifierItemProperties.OverlayIconPixmap) => _props.OverlayIconPixmap,
        nameof(StatusNotifierItemProperties.AttentionIconName) => _props.AttentionIconName,
        nameof(StatusNotifierItemProperties.AttentionIconPixmap) => _props.AttentionIconPixmap,
        nameof(StatusNotifierItemProperties.AttentionMovieName) => _props.AttentionMovieName,
        nameof(StatusNotifierItemProperties.ToolTip) => _props.ToolTip,
        _ => throw new DBusException(
            "org.freedesktop.DBus.Error.InvalidArgs",
            $"Unknown StatusNotifierItem property: {prop}"),
    });

    public Task<StatusNotifierItemProperties> GetAllAsync() =>
        Task.FromResult(_props);

    public Task SetAsync(string prop, object val) => Task.CompletedTask;

    public Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler)
    {
        _watchHandler = handler;
        return Task.FromResult<IDisposable>(new DisposableAction(() => _watchHandler = null));
    }

    public Task<IDisposable> WatchNewIconAsync(Action handler)
    {
        _newIconHandler = handler;
        return Task.FromResult<IDisposable>(new DisposableAction(() => _newIconHandler = null));
    }

    public Task<IDisposable> WatchNewToolTipAsync(Action handler)
    {
        _newToolTipHandler = handler;
        return Task.FromResult<IDisposable>(new DisposableAction(() => _newToolTipHandler = null));
    }

    public void UpdateIconPixmap((int width, int height, byte[] data)[] pixmap)
    {
        _props.IconPixmap = pixmap;
        _watchHandler?.Invoke(PropertyChanges.ForProperty(nameof(StatusNotifierItemProperties.IconPixmap), pixmap));
        _newIconHandler?.Invoke();
    }

    public void UpdateToolTip(
        (string icon, (int width, int height, byte[] data)[] pixmap, string title, string description) tooltip)
    {
        _props.ToolTip = tooltip;
        _watchHandler?.Invoke(PropertyChanges.ForProperty(nameof(StatusNotifierItemProperties.ToolTip), tooltip));
        _newToolTipHandler?.Invoke();
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
    event Action<int, int>? TooltipToggleRequested;

    void UpdateIcon(IReadOnlyList<IconLayout.Bar> bars);
    void ShowNotification(NotificationLevel level, string message);
}

internal sealed class NativeTray : INativeTray, IDisposable
{
    private readonly Connection _connection;
    private readonly StatusNotifierItem _item;
    private readonly DbusMenu _menu;
    private readonly ILogger<NativeTray> _logger;

    public event Action? SettingsRequested;
    public event Action? RefreshRequested;
    public event Action? ExitRequested;
    public event Action<int, int>? TooltipToggleRequested;

    public NativeTray(ILogger<NativeTray> logger)
    {
        _logger = logger;
        var address = Address.Session ?? throw new InvalidOperationException("D-Bus session bus is not available.");
        _connection = new Connection(address);
        _connection.ConnectAsync().GetAwaiter().GetResult();

        _item = new StatusNotifierItem(this);
        _menu = new DbusMenu(this);

        var serviceName = $"org.kde.StatusNotifierItem-{Environment.ProcessId}-1";

        _connection.RegisterServiceAsync(serviceName).GetAwaiter().GetResult();
        _connection.RegisterObjectAsync(_item).GetAwaiter().GetResult();
        _connection.RegisterObjectAsync(_menu).GetAwaiter().GetResult();

        IsStatusNotifierAvailable = RegisterWithWatcher(serviceName);

        var bars = new IconLayout.Bar[] { new(UsedPercent: null, Weight: 1.0, Provider: "None") };
        UpdateIcon(bars);
    }

    public bool IsStatusNotifierAvailable { get; }

    public void UpdateIcon(IReadOnlyList<IconLayout.Bar> bars)
    {
        var bitmap = IconBitmapRenderer.Render(bars);
        var pixmap = CreateIconPixmap(bitmap.Xor, bitmap.Width, bitmap.Height);
        _item.UpdateIconPixmap(pixmap);
    }

    public void ShowNotification(NotificationLevel level, string message)
    {
        _ = ShowNotificationAsync(level, message);
    }

    internal void RaiseSettingsRequested() => SettingsRequested?.Invoke();
    internal void RaiseRefreshRequested() => RefreshRequested?.Invoke();
    internal void RaiseExitRequested() => ExitRequested?.Invoke();
    internal void ToggleTooltip(int x = -1, int y = -1)
    {
        _logger.LogInformation("Tray activation received at x={X}; y={Y}.", x, y);
        TooltipToggleRequested?.Invoke(x, y);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private bool RegisterWithWatcher(string serviceName)
    {
        try
        {
            var proxy = _connection.CreateProxy<IStatusNotifierWatcher>(
                "org.kde.StatusNotifierWatcher",
                new ObjectPath("/StatusNotifierWatcher"));

            proxy.RegisterStatusNotifierItemAsync(serviceName).GetAwaiter().GetResult();
            _logger.LogInformation("Registered Linux tray icon with the StatusNotifier host.");
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "StatusNotifier host is unavailable. A fallback window will be used.");
            return false;
        }
    }

    private async Task ShowNotificationAsync(NotificationLevel level, string message)
    {
        try
        {
            var proxy = _connection.CreateProxy<INotifications>(
                "org.freedesktop.Notifications",
                new ObjectPath("/org/freedesktop/Notifications"));

            await proxy.NotifyAsync(
                "Usage Bar", 0, string.Empty,
                GetLevelTitle(level), message,
                [], new Dictionary<string, object>(), 5000).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not show a desktop notification.");
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
public interface IStatusNotifierWatcher : IDBusObject
{
    Task RegisterStatusNotifierItemAsync(string service);
}

[DBusInterface("org.freedesktop.Notifications")]
public interface INotifications : IDBusObject
{
    Task<uint> NotifyAsync(string appName, uint replacesId, string appIcon,
        string summary, string body, string[] actions,
        IDictionary<string, object> hints, int expireTimeout);
}
