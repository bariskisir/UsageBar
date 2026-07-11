using Gtk;
using System.Reflection;
using System.Text.Json;
using UsageBar.Core.Domain;
using UsageBar.Core.Infrastructure;
using UsageBar.Core.Tooltip;
using WebKit;

namespace UsageBar.Linux.Tooltip;
internal interface INativeTooltip
{
    void SetContent(IReadOnlyList<TooltipCard> cards, int scale);
    void ShowNearIcon();
    void Hide();
}

internal sealed class NativeTooltip : INativeTooltip, IDisposable
{
    private static int _glibInitialized;
    private readonly Window _window;
    private readonly WebView _webView;
    private readonly Lock _gate = new();
    private string _pendingJson = """{"cards":[]}""";
    private volatile bool _ready;
    public NativeTooltip()
    {
        if (Interlocked.CompareExchange(ref _glibInitialized, 1, 0) == 0)
        {
            Application.Init();
        }

        _window = new Window(WindowType.Popup);
        _window.SetDefaultSize(300, 200);
        _window.Decorated = false;
        _window.SkipTaskbarHint = true;
        _window.SkipPagerHint = true;
        _window.KeepAbove = true;
        _window.TypeHint = Gdk.WindowTypeHint.Tooltip;
        _window.FocusOutEvent += (_, _) => Hide();
        _webView = new WebView();
        _webView.SetSizeRequest(300, 200);
        _webView.DecidePolicy += OnDecidePolicy;
        _window.Add(_webView);
        var html = LoadHtml();
        _webView.LoadHtml(html, null);
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

    public void ShowNearIcon()
    {
        _window.ShowAll();
        _webView.GrabFocus();
    }

    public void Hide()
    {
        _window.Hide();
    }

    public void Dispose()
    {
        _window.Dispose();
        _webView.Dispose();
    }

    private void OnDecidePolicy(object o, DecidePolicyArgs args)
    {
        if (args.Decision is NavigationPolicyDecision nav)
        {
#pragma warning disable CS0612
            var uri = nav.Request?.Uri;
#pragma warning restore CS0612
            if (uri is not null && uri.StartsWith("callback://", StringComparison.Ordinal))
            {
                nav.Ignore();
                var msg = Uri.UnescapeDataString(uri["callback://".Length..]);
                OnScriptMessage(msg);
                return;
            }

            nav.Ignore();
        }
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
                // Adjust window size if needed
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
        return html.Replace("</head>", "<script>window.ipc={postMessage:(m)=>window.location.href='callback://'+encodeURIComponent(m),addMessageListener:()=>{}};</script></head>", StringComparison.Ordinal).Replace("{{CODEX_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Codex.codex.svg"), StringComparison.Ordinal).Replace("{{CLAUDE_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Claude.claude.svg"), StringComparison.Ordinal).Replace("{{ELEVENLABS_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.ElevenLabs.elevenlabs.svg"), StringComparison.Ordinal).Replace("{{KILO_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Kilo.kilo.svg"), StringComparison.Ordinal).Replace("{{DEEPSEEK_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.DeepSeek.deepseek.svg"), StringComparison.Ordinal).Replace("{{OPENROUTER_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.OpenRouter.openrouter.svg"), StringComparison.Ordinal).Replace("{{MOONSHOT_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Moonshot.moonshot.svg"), StringComparison.Ordinal).Replace("{{DEEPGRAM_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Deepgram.deepgram.svg"), StringComparison.Ordinal).Replace("{{OPENAI_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.OpenAI.openai.svg"), StringComparison.Ordinal).Replace("{{VENICE_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Venice.venice.svg"), StringComparison.Ordinal).Replace("{{COPILOT_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Copilot.copilot.svg"), StringComparison.Ordinal).Replace("{{CROF_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Crof.crof.svg"), StringComparison.Ordinal).Replace("{{CODEBUFF_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Codebuff.codebuff.svg"), StringComparison.Ordinal).Replace("{{WARP_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Warp.warp.svg"), StringComparison.Ordinal).Replace("{{ZAI_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Zai.zai.svg"), StringComparison.Ordinal).Replace("{{SYNTHETIC_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Synthetic.synthetic.svg"), StringComparison.Ordinal).Replace("{{CHUTES_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Chutes.chutes.svg"), StringComparison.Ordinal).Replace("{{MINIMAX_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.MiniMax.minimax.svg"), StringComparison.Ordinal).Replace("{{POE_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Poe.poe.svg"), StringComparison.Ordinal).Replace("{{ALIBABA_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Alibaba.alibaba.svg"), StringComparison.Ordinal).Replace("{{ZENMUX_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.ZenMux.zenmux.svg"), StringComparison.Ordinal).Replace("{{ANTIGRAVITY_ICON}}", ReadSvgDataUri(assembly, "UsageBar.Core.Providers.Antigravity.antigravity.svg"), StringComparison.Ordinal);
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
}