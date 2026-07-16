using AppKit;
using CoreGraphics;
using Foundation;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using UsageBar.Core.Infrastructure;
using UsageBar.Core.Settings;
using WebKit;

namespace UsageBar.MacOS.Settings;

internal sealed class SettingsPanel : IDisposable
{
    private sealed class ScriptMessageHandler(Action<string> onMessage) : NSObject, IWKScriptMessageHandler
    {
        public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
        {
            if (message.Body is NSString body)
            {
                onMessage(body.ToString());
            }
        }
    }

    private readonly SettingsController _controller;
    private readonly ILogger<SettingsPanel> _logger;
    private readonly NSWindow _window;
    private readonly WKWebView _webView;
    private readonly ScriptMessageHandler _messageHandler;

    public SettingsPanel(SettingsController controller, ILogger<SettingsPanel> logger)
    {
        _controller = controller;
        _logger = logger;
        var configuration = new WKWebViewConfiguration();
        _messageHandler = new ScriptMessageHandler(OnMessage);
        configuration.UserContentController.AddScriptMessageHandler(_messageHandler, "ipc");
        configuration.UserContentController.AddUserScript(new WKUserScript(
            new NSString("window.ipc={_listener:null,postMessage:(m)=>window.webkit.messageHandlers.ipc.postMessage(typeof m==='string'?m:JSON.stringify(m)),addMessageListener:function(cb){this._listener=cb},_dispatch:function(m){if(this._listener)this._listener(m)}};"),
            WKUserScriptInjectionTime.AtDocumentStart,
            true));
        _webView = new WKWebView(new CGRect(0, 0, 506, 540), configuration);
        _window = new NSWindow(
            new CGRect(0, 0, 506, 540),
            NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable,
            NSBackingStore.Buffered,
            false)
        {
            Title = "Usage Bar Settings",
            ContentView = _webView,
        };
        _window.ReleaseWhenClosed(false);
        _window.Center();
        _webView.LoadHtmlString(new NSString(ReadSettingsHtml()), null);
    }

    public void Show()
    {
        NSApplication.SharedApplication.Activate();
        _window.MakeKeyAndOrderFront(null);
    }

    public void Dispose()
    {
        _webView.Configuration.UserContentController.RemoveScriptMessageHandler("ipc");
        _webView.Dispose();
        _window.Dispose();
        _messageHandler.Dispose();
    }

    private async void OnMessage(string json)
    {
        try
        {
            var message = JsonSerializer.Deserialize(json, SettingsIpcJsonContext.Default.SettingsInboundMessage);
            if (message is null)
            {
                return;
            }

            switch (message.Type)
            {
                case "ready":
                    Post(await _controller.GetStateAsync().ConfigureAwait(true), SettingsIpcJsonContext.Default.SettingsStateMessage);
                    break;
                case "settings-save" when message.Settings is not null:
                    await _controller.SaveAsync(message.Settings, message.EnvironmentSourcedKeys).ConfigureAwait(true);
                    Post(new SettingsStatusMessage("settings-saved"), SettingsIpcJsonContext.Default.SettingsStatusMessage);
                    break;
                case "close":
                    _window.OrderOut(null);
                    break;
                case "test-notification":
                    await _controller.SendTestNotificationAsync().ConfigureAwait(true);
                    break;
                case "test-start-window" when message.Settings is not null:
                    var testResult = await _controller.TestStartWindowAsync(message.Settings).ConfigureAwait(true);
                    Post(new SettingsStatusMessage("start-window-test-result", testResult), SettingsIpcJsonContext.Default.SettingsStatusMessage);
                    break;
                case "check-update":
                    var result = await _controller.CheckForUpdatesAsync().ConfigureAwait(true);
                    var text = result.HasUpdate
                        ? $"Usage Bar {result.LatestVersion} available"
                        : result.ErrorMessage ?? $"Usage Bar is up to date ({result.LatestVersion}).";
                    Post(new SettingsStatusMessage("update-result", text), SettingsIpcJsonContext.Default.SettingsStatusMessage);
                    if (result.HasUpdate)
                    {
                        NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl("https://github.com/bariskisir/usagebar/releases/latest"));
                    }

                    break;
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "macOS settings command failed.");
            Post(new SettingsStatusMessage("settings-error", "The command could not be completed."), SettingsIpcJsonContext.Default.SettingsStatusMessage);
        }
    }

    private void Post<T>(T message, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        var json = JsonSerializer.Serialize(message, typeInfo);
        _webView.EvaluateJavaScript($"window.ipc._dispatch({json})", null!);
    }

    private static string ReadSettingsHtml()
    {
        var assembly = typeof(EmbeddedPageLoader).Assembly;
        return EmbeddedPageLoader.Load(
            assembly,
            "UsageBar.Core.Frontend.settings.html",
            "UsageBar.Core.Frontend.settings.css",
            "UsageBar.Core.Frontend.settings.js",
            "{{SETTINGS_CSS}}",
            "{{SETTINGS_JS}}");
    }
}
