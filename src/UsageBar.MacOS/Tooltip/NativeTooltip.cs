using AppKit;
using CoreGraphics;
using Foundation;
using System.Reflection;
using System.Text;
using System.Text.Json;
using UsageBar.Core.Domain;
using UsageBar.Core.Infrastructure;
using UsageBar.Core.Tooltip;
using UsageBar.MacOS.Tray;
using WebKit;

namespace UsageBar.MacOS.Tooltip;
internal interface INativeTooltip
{
    void SetContent(IReadOnlyList<TooltipCard> cards, int scale);
    void ShowNearStatusItem(NSStatusItem statusItem);
    void Hide();
}

internal sealed class NativeTooltip : INativeTooltip, IDisposable
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

    private readonly NSPopover _popover;
    private readonly WKWebView _webView;
    private readonly Lock _gate = new();
    private string _pendingJson = """{"cards":[]}""";
    private volatile bool _ready;
    private NSStatusItem? _currentStatusItem;
    public NativeTooltip()
    {
        var config = new WKWebViewConfiguration();
        config.UserContentController.AddScriptMessageHandler(new ScriptMessageHandler(OnScriptMessage), "ipc");
        config.UserContentController.AddUserScript(new WKUserScript(new NSString("window.ipc={postMessage:(m)=>window.webkit.messageHandlers.ipc.postMessage(typeof m==='string'?m:JSON.stringify(m)),addMessageListener:()=>{}};"), WKUserScriptInjectionTime.AtDocumentStart, true));
        _webView = new WKWebView(CGRect.Empty, config);
        var controller = new NSViewController();
        controller.View = new NSView(new CGRect(0, 0, 300, 200));
        controller.View.AddSubview(_webView);
        _webView.Frame = controller.View.Bounds;
        _webView.AutoresizingMask = NSViewResizingMask.HeightSizable | NSViewResizingMask.WidthSizable;
        _popover = new NSPopover
        {
            Behavior = NSPopoverBehavior.Transient,
            ContentViewController = controller,
        };
        var html = LoadHtml();
        _webView.LoadHtmlString(new NSString(html), null);
    }

    public void SetContent(IReadOnlyList<TooltipCard> cards, int scale)
    {
        var json = JsonSerializer.Serialize(new TooltipPayload(cards, scale), TooltipJsonContext.Default.TooltipPayload);
        lock (_gate)
        {
            _pendingJson = json;
            if (_ready)
            {
                SendToJs(json);
            }
        }
    }

    public void ShowNearStatusItem(NSStatusItem statusItem)
    {
        _currentStatusItem = statusItem;
        var button = statusItem.Button;
        _popover.Show(button.Bounds, button, NSRectEdge.MaxYEdge);
    }

    public void Hide()
    {
        _popover.Close();
    }

    public void Dispose()
    {
        _webView.Dispose();
        _popover.Dispose();
    }

    private void OnScriptMessage(string body)
    {
        try
        {
            using (var doc = JsonDocument.Parse(body))
            {
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type == "ready")
                {
                    lock (_gate)
                    {
                        _ready = true;
                        if (!string.IsNullOrEmpty(_pendingJson))
                        {
                            SendToJs(_pendingJson);
                        }
                    }

                    return;
                }

                if (type == "size" && _currentStatusItem is not null)
                {
                    _popover.Show(_currentStatusItem.Button.Bounds, _currentStatusItem.Button, NSRectEdge.MaxYEdge);
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    private static string LoadHtml()
    {
        var assembly = typeof(EmbeddedPageLoader).Assembly;
        var html = EmbeddedPageLoader.Load(assembly, "UsageBar.Core.Frontend.index.html", "UsageBar.Core.Frontend.tooltip.css", "UsageBar.Core.Frontend.tooltip.js", "<!-- TOOLTIP_CSS -->", "// TOOLTIP_JS");
        return html.Replace("{{CODEX_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Codex.codex.svg"), StringComparison.Ordinal).Replace("{{CLAUDE_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Claude.claude.svg"), StringComparison.Ordinal).Replace("{{ELEVENLABS_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.ElevenLabs.elevenlabs.svg"), StringComparison.Ordinal).Replace("{{KILO_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Kilo.kilo.svg"), StringComparison.Ordinal).Replace("{{DEEPSEEK_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.DeepSeek.deepseek.svg"), StringComparison.Ordinal).Replace("{{OPENROUTER_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.OpenRouter.openrouter.svg"), StringComparison.Ordinal).Replace("{{MOONSHOT_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Moonshot.moonshot.svg"), StringComparison.Ordinal).Replace("{{DEEPGRAM_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Deepgram.deepgram.svg"), StringComparison.Ordinal).Replace("{{OPENAI_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.OpenAI.openai.svg"), StringComparison.Ordinal).Replace("{{VENICE_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Venice.venice.svg"), StringComparison.Ordinal).Replace("{{COPILOT_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Copilot.copilot.svg"), StringComparison.Ordinal).Replace("{{CROF_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Crof.crof.svg"), StringComparison.Ordinal).Replace("{{CODEBUFF_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Codebuff.codebuff.svg"), StringComparison.Ordinal).Replace("{{WARP_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Warp.warp.svg"), StringComparison.Ordinal).Replace("{{ZAI_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Zai.zai.svg"), StringComparison.Ordinal).Replace("{{SYNTHETIC_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Synthetic.synthetic.svg"), StringComparison.Ordinal).Replace("{{CHUTES_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Chutes.chutes.svg"), StringComparison.Ordinal).Replace("{{MINIMAX_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.MiniMax.minimax.svg"), StringComparison.Ordinal).Replace("{{POE_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Poe.poe.svg"), StringComparison.Ordinal).Replace("{{ALIBABA_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Alibaba.alibaba.svg"), StringComparison.Ordinal).Replace("{{ZENMUX_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.ZenMux.zenmux.svg"), StringComparison.Ordinal).Replace("{{ANTIGRAVITY_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Antigravity.antigravity.svg"), StringComparison.Ordinal);
    }

    private static string ReadSvgDataUri(Assembly assembly, string resourceName)
    {
        using (var stream = assembly.GetManifestResourceStream(resourceName))
        {
            if (stream is null)
            {
                return string.Empty;
            }

            using (var reader = new StreamReader(stream))
            {
                var svg = reader.ReadToEnd();
                var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
                return $"data:image/svg+xml;base64,{base64}";
            }
        }
    }

    private void SendToJs(string json)
    {
        var escaped = json.Replace("\\", "\\\\").Replace("'", "\\'");
        _webView.EvaluateJavaScript($"window.__render(JSON.parse('{escaped}'))", null!);
    }
}