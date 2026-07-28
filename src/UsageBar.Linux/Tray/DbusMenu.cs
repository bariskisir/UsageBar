using Tmds.DBus;

namespace UsageBar.Linux.Tray;

[DBusInterface("com.canonical.dbusmenu", PropertyType = typeof(DbusMenuProperties))]
public interface IDbusMenu : IDBusObject
{
    Task<(uint revision, (int id, IDictionary<string, object> properties, object[] children) layout)> GetLayoutAsync(
        int parentId,
        int recursionDepth,
        string[] propertyNames);

    Task<(int id, IDictionary<string, object> properties)[]> GetGroupPropertiesAsync(
        int[] ids,
        string[] propertyNames);

    Task<object> GetPropertyAsync(int id, string name);
    Task EventAsync(int id, string eventId, object data, uint timestamp);
    Task<int[]> EventGroupAsync((int id, string eventId, object data, uint timestamp)[] events);
    Task<bool> AboutToShowAsync(int id);
    Task<(int[] updatesNeeded, int[] idErrors)> AboutToShowGroupAsync(int[] ids);
    Task<object> GetAsync(string prop);
    Task<DbusMenuProperties> GetAllAsync();
    Task SetAsync(string prop, object val);
    Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler);
}

[Dictionary]
public sealed class DbusMenuProperties
{
    [Property(Access = PropertyAccess.Read)]
    public uint Version = 4;

    [Property(Access = PropertyAccess.Read)]
    public string TextDirection = "ltr";

    [Property(Access = PropertyAccess.Read)]
    public string Status = "normal";

    [Property(Access = PropertyAccess.Read)]
    public string[] IconThemePath = [];
}

internal sealed class DbusMenu : IDbusMenu
{
    private const int RootId = 0;
    private const int SettingsId = 3;
    private const int RefreshId = 7;
    private const int QuitId = 5;

    private readonly NativeTray _tray;
    private readonly DbusMenuProperties _properties = new();

    public DbusMenu(NativeTray tray)
    {
        _tray = tray;
        ObjectPath = new ObjectPath("/MenuBar");
    }

    public ObjectPath ObjectPath { get; }

    public Task<(uint revision, (int id, IDictionary<string, object> properties, object[] children) layout)> GetLayoutAsync(
        int parentId,
        int recursionDepth,
        string[] propertyNames)
    {
        var layout = parentId == RootId
            ? CreateRootLayout(recursionDepth)
            : CreateItemLayout(parentId);
        return Task.FromResult((1u, layout));
    }

    public Task<(int id, IDictionary<string, object> properties)[]> GetGroupPropertiesAsync(
        int[] ids,
        string[] propertyNames)
    {
        var requestedIds = ids.Length == 0
            ? [RootId, SettingsId, RefreshId, QuitId]
            : ids;
        var result = requestedIds
            .Where(IsKnownId)
            .Select(id => (id, GetProperties(id)))
            .ToArray();
        return Task.FromResult(result);
    }

    public Task<object> GetPropertyAsync(int id, string name)
    {
        var properties = GetProperties(id);
        if (!properties.TryGetValue(name, out var value))
        {
            throw new DBusException(
                "com.canonical.dbusmenu.Error.UnknownProperty",
                $"Unknown menu property '{name}' for item {id}.");
        }

        return Task.FromResult(value);
    }

    public Task EventAsync(int id, string eventId, object data, uint timestamp)
    {
        if (string.Equals(eventId, "clicked", StringComparison.Ordinal))
        {
            Activate(id);
        }

        return Task.CompletedTask;
    }

    public Task<int[]> EventGroupAsync((int id, string eventId, object data, uint timestamp)[] events)
    {
        var errors = new List<int>();
        foreach (var menuEvent in events)
        {
            if (!IsKnownId(menuEvent.id))
            {
                errors.Add(menuEvent.id);
                continue;
            }

            if (string.Equals(menuEvent.eventId, "clicked", StringComparison.Ordinal))
            {
                Activate(menuEvent.id);
            }
        }

        return Task.FromResult(errors.ToArray());
    }

    public Task<bool> AboutToShowAsync(int id) => Task.FromResult(false);

    public Task<(int[] updatesNeeded, int[] idErrors)> AboutToShowGroupAsync(int[] ids) =>
        Task.FromResult((Array.Empty<int>(), ids.Where(id => !IsKnownId(id)).ToArray()));

    public Task<object> GetAsync(string prop) => Task.FromResult(prop switch
    {
        nameof(DbusMenuProperties.Version) => (object)_properties.Version,
        nameof(DbusMenuProperties.TextDirection) => _properties.TextDirection,
        nameof(DbusMenuProperties.Status) => _properties.Status,
        nameof(DbusMenuProperties.IconThemePath) => _properties.IconThemePath,
        _ => throw new DBusException(
            "org.freedesktop.DBus.Error.InvalidArgs",
            $"Unknown DBusMenu property: {prop}"),
    });

    public Task<DbusMenuProperties> GetAllAsync() => Task.FromResult(_properties);

    public Task SetAsync(string prop, object val) => Task.CompletedTask;

    public Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler) =>
        Task.FromResult<IDisposable>(NoopDisposable.Instance);

    private static (int id, IDictionary<string, object> properties, object[] children) CreateRootLayout(
        int recursionDepth)
    {
        var children = recursionDepth == 0
            ? []
            : new object[]
            {
                CreateItemLayout(SettingsId),
                CreateItemLayout(RefreshId),
                CreateItemLayout(QuitId),
            };

        return (RootId, GetProperties(RootId), children);
    }

    private static (int id, IDictionary<string, object> properties, object[] children) CreateItemLayout(int id) =>
        (id, GetProperties(id), []);

    private static IDictionary<string, object> GetProperties(int id) => id switch
    {
        RootId => new Dictionary<string, object>
        {
            ["children-display"] = "submenu",
        },
        SettingsId => CreateActionProperties("Settings"),
        RefreshId => CreateActionProperties("Refresh"),
        QuitId => CreateActionProperties("Exit"),
        _ => throw new DBusException(
            "com.canonical.dbusmenu.Error.UnknownItem",
            $"Unknown menu item: {id}"),
    };

    private static Dictionary<string, object> CreateActionProperties(string label) => new()
    {
        ["label"] = label,
        ["enabled"] = true,
        ["visible"] = true,
    };

    private static bool IsKnownId(int id) => id is RootId or SettingsId or RefreshId or QuitId;

    private void Activate(int id)
    {
        switch (id)
        {
            case SettingsId:
                _tray.RaiseSettingsRequested();
                break;
            case RefreshId:
                _tray.RaiseRefreshRequested();
                break;
            case QuitId:
                _tray.RaiseExitRequested();
                break;
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
