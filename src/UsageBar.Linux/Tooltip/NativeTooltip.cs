using Gtk;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text.Json;
using UsageBar.Core.Domain;
using UsageBar.Core.Infrastructure;
using UsageBar.Core.Tooltip;
using UsageBar.Linux.Infrastructure;
using WebKit;

namespace UsageBar.Linux.Tooltip;
internal interface INativeTooltip
{
    void SetContent(IReadOnlyList<TooltipCard> cards, int scale);
    void ShowNearIcon(int x = -1, int y = -1);
    void ToggleNearIcon(int x = -1, int y = -1);
    void Hide();
}

internal sealed class NativeTooltip : INativeTooltip, IDisposable
{
    private readonly GtkDispatcher _dispatcher;
    private readonly WebKitMessageBridge _bridge;
    private readonly ILogger<NativeTooltip> _logger;
    private readonly Window _window;
    private readonly WebView _webView;
    private readonly Lock _gate = new();
    private string _pendingJson = """{"cards":[]}""";
    private volatile bool _ready;
    private int _windowWidth = 300;
    private int _windowHeight = 200;
    private int _anchorX = -1;
    private int _anchorY = -1;
    private long _lastFocusOutTick;
    private bool _isVisible;
    public NativeTooltip(GtkDispatcher dispatcher, ILogger<NativeTooltip> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
        _window = new Window(WindowType.Toplevel);
        _window.SetDefaultSize(300, 200);
        _window.Decorated = false;
        _window.SkipTaskbarHint = true;
        _window.SkipPagerHint = true;
        _window.KeepAbove = true;
        _window.TypeHint = Gdk.WindowTypeHint.Tooltip;
        _window.AppPaintable = true;
        if (_window.Screen?.RgbaVisual is { } rgbaVisual)
        {
            _window.Visual = rgbaVisual;
        }
        _window.FocusOutEvent += (_, _) =>
        {
            _lastFocusOutTick = Environment.TickCount64;
            HideCore();
        };
        _bridge = new WebKitMessageBridge();
        _bridge.MessageReceived += OnScriptMessage;
        _webView = new WebView(_bridge.ContentManager);
        _webView.SetBackgroundColor(new Gdk.RGBA
        {
            Red = 0,
            Green = 0,
            Blue = 0,
            Alpha = 0,
        });
        _webView.SetSizeRequest(300, 200);
        _webView.LoadFailed += (_, args) =>
            _logger.LogWarning(
                "Tooltip WebKit load failed: event={LoadEvent}; uri={FailingUri}",
                args.LoadEvent,
                args.FailingUri);
        _window.Add(_webView);
        var html = LoadHtml();
        _webView.LoadHtml(html, null);
    }

    public void SetContent(IReadOnlyList<TooltipCard> cards, int scale)
    {
        var json = JsonSerializer.Serialize(new TooltipPayload(cards, scale), TooltipJsonContext.Default.TooltipPayload);
        var shouldSend = false;
        lock (_gate)
        {
            _pendingJson = json;
            shouldSend = _ready;
        }

        if (shouldSend)
        {
            _dispatcher.Invoke(() => SendToJs(json));
        }
    }

    public void ShowNearIcon(int x = -1, int y = -1)
    {
        _dispatcher.Invoke(() => ShowCore(x, y));
    }

    public void ToggleNearIcon(int x = -1, int y = -1)
    {
        _dispatcher.Invoke(() =>
        {
            if (_isVisible)
            {
                HideCore();
                return;
            }

            // Clicking the panel icon can move focus away from the tooltip
            // just before StatusNotifierItem.Activate reaches the app. In that
            // case FocusOut already hid it, so this activation is the closing
            // half of the toggle and must not immediately show it again.
            var elapsedSinceFocusOut = Environment.TickCount64 - _lastFocusOutTick;
            if (elapsedSinceFocusOut is >= 0 and <= 500)
            {
                return;
            }

            ShowCore(x, y);
        });
    }

    public void Hide()
    {
        _dispatcher.Invoke(HideCore);
    }

    public void SetTransientFor(Window parent)
    {
        _window.TransientFor = parent;
    }

    public void Dispose()
    {
        _bridge.Dispose();
        _webView.Dispose();
        _window.Dispose();
    }

    private void OnScriptMessage(string text)
    {
        try
        {
            using (var doc = JsonDocument.Parse(text))
            {
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type == "ready")
                {
                    _logger.LogInformation("Tooltip WebKit bridge ready.");
                    lock (_gate)
                    {
                        _ready = true;
                        if (!string.IsNullOrEmpty(_pendingJson))
                        {
                            SendToJs(_pendingJson);
                        }
                    }
                }

                if (type == "size")
                {
                    var width = root.TryGetProperty("width", out var widthElement)
                        ? widthElement.GetInt32()
                        : _windowWidth;
                    var height = root.TryGetProperty("height", out var heightElement)
                        ? heightElement.GetInt32()
                        : _windowHeight;
                    ResizeToContent(width, height);
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
        var html = EmbeddedPageLoader.Load(
            assembly,
            "UsageBar.Core.Frontend.index.html",
            "UsageBar.Core.Frontend.tooltip.css",
            "UsageBar.Core.Frontend.tooltip.js",
            "{{TOOLTIP_CSS}}",
            "{{TOOLTIP_JS}}");
        return html.Replace(
            "</head>",
            $"<style>:root,html,body{{background:transparent!important}}</style><script>{WebKitMessageBridge.CreateJavascript()}</script></head>",
            StringComparison.Ordinal).Replace("{{CODEX_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Codex.codex.svg"), StringComparison.Ordinal).Replace("{{CLAUDE_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Claude.claude.svg"), StringComparison.Ordinal).Replace("{{ELEVENLABS_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.ElevenLabs.elevenlabs.svg"), StringComparison.Ordinal).Replace("{{KILO_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Kilo.kilo.svg"), StringComparison.Ordinal).Replace("{{DEEPSEEK_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.DeepSeek.deepseek.svg"), StringComparison.Ordinal).Replace("{{OPENROUTER_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.OpenRouter.openrouter.svg"), StringComparison.Ordinal).Replace("{{MOONSHOT_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Moonshot.moonshot.svg"), StringComparison.Ordinal).Replace("{{DEEPGRAM_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Deepgram.deepgram.svg"), StringComparison.Ordinal).Replace("{{OPENAI_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.OpenAI.openai.svg"), StringComparison.Ordinal).Replace("{{VENICE_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Venice.venice.svg"), StringComparison.Ordinal).Replace("{{COPILOT_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Copilot.copilot.svg"), StringComparison.Ordinal).Replace("{{CROF_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Crof.crof.svg"), StringComparison.Ordinal).Replace("{{CODEBUFF_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Codebuff.codebuff.svg"), StringComparison.Ordinal).Replace("{{WARP_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Warp.warp.svg"), StringComparison.Ordinal).Replace("{{ZAI_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Zai.zai.svg"), StringComparison.Ordinal).Replace("{{SYNTHETIC_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Synthetic.synthetic.svg"), StringComparison.Ordinal).Replace("{{CHUTES_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Chutes.chutes.svg"), StringComparison.Ordinal).Replace("{{MINIMAX_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.MiniMax.minimax.svg"), StringComparison.Ordinal).Replace("{{POE_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Poe.poe.svg"), StringComparison.Ordinal).Replace("{{ALIBABA_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Alibaba.alibaba.svg"), StringComparison.Ordinal).Replace("{{ZENMUX_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.ZenMux.zenmux.svg"), StringComparison.Ordinal).Replace("{{ANTIGRAVITY_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Antigravity.antigravity.svg"), StringComparison.Ordinal).Replace("{{COMMANDCODE_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.CommandCode.commandcode.svg"), StringComparison.Ordinal);
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
                var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));
                return $"data:image/svg+xml;base64,{base64}";
            }
        }
    }

    private void SendToJs(string json)
    {
        var escaped = json.Replace("\\", "\\\\").Replace("'", "\\'");
        _webView.RunJavascript($"window.__render(JSON.parse('{escaped}'))");
    }

    private void ShowCore(int x, int y)
    {
        _anchorX = x;
        _anchorY = y;
        PositionNearIcon(x, y);
        _window.ShowAll();
        _isVisible = true;
        PositionNearIcon(x, y);
        _webView.GrabFocus();
    }

    private void HideCore()
    {
        _isVisible = false;
        _window.Hide();
    }

    private void PositionNearIcon(int x, int y)
    {
        const int panelGap = 12;

        var display = Gdk.Display.Default;
        var monitor = display?.GetMonitorAtPoint(Math.Max(0, x), Math.Max(0, y))
            ?? display?.PrimaryMonitor;
        var workarea = monitor?.Workarea ?? new Gdk.Rectangle(0, 0, _windowWidth, _windowHeight);
        var anchorX = x >= 0 ? x : workarea.X + workarea.Width - 32;
        var anchorY = y >= 0 ? y : workarea.Y + 16;
        var minLeft = workarea.X + 8;
        var maxLeft = Math.Max(minLeft, workarea.X + workarea.Width - _windowWidth - 8);
        var minTop = workarea.Y + 8;
        var maxTop = Math.Max(minTop, workarea.Y + workarea.Height - _windowHeight - 8);
        var left = Math.Clamp(anchorX - (_windowWidth / 2), minLeft, maxLeft);
        var top = Math.Clamp(anchorY + panelGap, minTop, maxTop);

        _logger.LogInformation(
            "Positioning tooltip: display={DisplayName}; anchor=({AnchorX},{AnchorY}); position=({Left},{Top}); size={Width}x{Height}.",
            display?.Name ?? "unknown",
            x,
            y,
            left,
            top,
            _windowWidth,
            _windowHeight);
        _window.Move(left, top);
    }

    private void ResizeToContent(int width, int height)
    {
        _dispatcher.Invoke(() =>
        {
            _windowWidth = Math.Clamp(width, 120, 900);
            _windowHeight = Math.Clamp(height, 1, 1000);
            _webView.SetSizeRequest(_windowWidth, _windowHeight);
            _window.Resize(_windowWidth, _windowHeight);
            PositionNearIcon(_anchorX, _anchorY);
        });
    }
}
