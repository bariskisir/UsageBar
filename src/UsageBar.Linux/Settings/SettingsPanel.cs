using Gtk;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using UsageBar.Core.Infrastructure;
using UsageBar.Core.Settings;
using WebKit;

namespace UsageBar.Linux.Settings;

internal sealed class SettingsPanel : IDisposable
{
    private readonly SettingsController _controller;
    private readonly ILogger<SettingsPanel> _logger;
    private readonly Window _window;
    private readonly WebView _webView;

    public SettingsPanel(SettingsController controller, ILogger<SettingsPanel> logger)
    {
        _controller = controller;
        _logger = logger;
        _window = new Window("Usage Bar Settings");
        _window.SetDefaultSize(506, 540);
        _window.DeleteEvent += (_, args) =>
        {
            args.RetVal = true;
            _window.Hide();
        };
        _webView = new WebView();
        _webView.DecidePolicy += OnDecidePolicy;
        _window.Add(_webView);
        _webView.LoadHtml(ReadSettingsHtml(), null);
    }

    public void Show()
    {
        _window.ShowAll();
        _window.Present();
    }

    public void Dispose()
    {
        _webView.Dispose();
        _window.Dispose();
    }

    private void OnDecidePolicy(object sender, DecidePolicyArgs args)
    {
        if (args.Decision is not NavigationPolicyDecision navigation)
        {
            return;
        }
#pragma warning disable CS0612
        var uri = navigation.Request?.Uri;
#pragma warning restore CS0612
        if (uri is not null && uri.StartsWith("callback://", StringComparison.Ordinal))
        {
            navigation.Ignore();
            OnMessage(Uri.UnescapeDataString(uri["callback://".Length..]));
        }
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
                    _window.Hide();
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
                        Process.Start(new ProcessStartInfo(
                            "https://github.com/bariskisir/usagebar/releases/latest")
                        { UseShellExecute = true });
                    }
                    break;
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Linux settings command failed.");
            Post(new SettingsStatusMessage("settings-error", "The command could not be completed."), SettingsIpcJsonContext.Default.SettingsStatusMessage);
        }
    }

    private void Post<T>(T message, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        var json = JsonSerializer.Serialize(message, typeInfo);
        _webView.RunJavascript($"window.ipc._dispatch({json})");
    }

    private static string ReadSettingsHtml()
    {
        var assembly = typeof(EmbeddedPageLoader).Assembly;
        var html = EmbeddedPageLoader.Load(
            assembly,
            "UsageBar.Core.Frontend.settings.html",
            "UsageBar.Core.Frontend.settings.css",
            "UsageBar.Core.Frontend.settings.js",
            "{{SETTINGS_CSS}}",
            "{{SETTINGS_JS}}");
        return html.Replace(
            "</head>",
            "<script>window.ipc={_listener:null,postMessage:(m)=>window.location.href='callback://'+encodeURIComponent(m),addMessageListener:function(cb){this._listener=cb},_dispatch:function(m){if(this._listener)this._listener(m)}};</script></head>",
            StringComparison.Ordinal);
    }
}
