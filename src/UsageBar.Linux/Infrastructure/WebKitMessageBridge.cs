using System.Runtime.InteropServices;
using WebKit;

namespace UsageBar.Linux.Infrastructure;

internal sealed class WebKitMessageBridge : IDisposable
{
    private const string HandlerName = "usagebar";
    private readonly nint _glibLibrary;
    private readonly nint _gobjectLibrary;
    private readonly nint _javascriptCoreLibrary;
    private readonly nint _webkitLibrary;
    private readonly SignalConnectData _signalConnectData;
    private readonly SignalHandlerDisconnect _signalHandlerDisconnect;
    private readonly JavascriptResultGetValue _javascriptResultGetValue;
    private readonly JavascriptValueToString _javascriptValueToString;
    private readonly GlibFree _glibFree;
    private readonly ScriptMessageCallback _scriptMessageCallback;
    private readonly ulong _signalHandlerId;
    private bool _disposed;

    public WebKitMessageBridge()
    {
        _glibLibrary = LoadLibrary("libglib-2.0.so.0");
        _gobjectLibrary = LoadLibrary("libgobject-2.0.so.0");
        _webkitLibrary = LoadLibrary("libwebkit2gtk-4.1.so.0", "libwebkit2gtk-4.0.so.37");
        _javascriptCoreLibrary = LoadLibrary(
            "libjavascriptcoregtk-4.1.so.0",
            "libjavascriptcoregtk-4.0.so.18");

        _signalConnectData = GetDelegate<SignalConnectData>(_gobjectLibrary, "g_signal_connect_data");
        _signalHandlerDisconnect = GetDelegate<SignalHandlerDisconnect>(_gobjectLibrary, "g_signal_handler_disconnect");
        _javascriptResultGetValue = GetDelegate<JavascriptResultGetValue>(
            _webkitLibrary,
            "webkit_javascript_result_get_js_value");
        _javascriptValueToString = GetDelegate<JavascriptValueToString>(
            _javascriptCoreLibrary,
            "jsc_value_to_string");
        _glibFree = GetDelegate<GlibFree>(_glibLibrary, "g_free");

        ContentManager = new UserContentManager();
        _scriptMessageCallback = OnScriptMessageReceived;
        _signalHandlerId = _signalConnectData(
            ContentManager.Handle,
            $"script-message-received::{HandlerName}",
            _scriptMessageCallback,
            nint.Zero,
            nint.Zero,
            0);
        if (_signalHandlerId == 0)
        {
            throw new InvalidOperationException("Could not connect the UsageBar WebKit message signal.");
        }

        if (!ContentManager.RegisterScriptMessageHandler(HandlerName))
        {
            throw new InvalidOperationException("Could not register the UsageBar WebKit message handler.");
        }
    }

    public event Action<string>? MessageReceived;

    public UserContentManager ContentManager { get; }

    public static string CreateJavascript(string additionalMembers = "") =>
        $"window.ipc={{postMessage:function(m){{window.webkit.messageHandlers.{HandlerName}.postMessage(String(m));}},addMessageListener:function(){{}}{additionalMembers}}};";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _signalHandlerDisconnect(ContentManager.Handle, _signalHandlerId);
        ContentManager.UnregisterScriptMessageHandler(HandlerName);
        ContentManager.Dispose();
    }

    private void OnScriptMessageReceived(nint manager, nint javascriptResult, nint userData)
    {
        nint messagePointer = nint.Zero;
        try
        {
            var javascriptValue = _javascriptResultGetValue(javascriptResult);
            messagePointer = _javascriptValueToString(javascriptValue);
            var message = Marshal.PtrToStringUTF8(messagePointer);
            if (message is not null)
            {
                MessageReceived?.Invoke(message);
            }
        }
        catch
        {
            // Exceptions must never cross a native GLib signal boundary.
        }
        finally
        {
            if (messagePointer != nint.Zero)
            {
                _glibFree(messagePointer);
            }
        }
    }

    private static nint LoadLibrary(params string[] names)
    {
        foreach (var name in names)
        {
            if (NativeLibrary.TryLoad(name, out var handle))
            {
                return handle;
            }
        }

        throw new DllNotFoundException($"Could not load any of: {string.Join(", ", names)}");
    }

    private static T GetDelegate<T>(nint library, string name)
        where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(
            NativeLibrary.GetExport(library, name));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ScriptMessageCallback(nint manager, nint javascriptResult, nint userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong SignalConnectData(
        nint instance,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string detailedSignal,
        ScriptMessageCallback callback,
        nint data,
        nint destroyData,
        int connectFlags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SignalHandlerDisconnect(nint instance, ulong handlerId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint JavascriptResultGetValue(nint javascriptResult);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint JavascriptValueToString(nint javascriptValue);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GlibFree(nint memory);
}
