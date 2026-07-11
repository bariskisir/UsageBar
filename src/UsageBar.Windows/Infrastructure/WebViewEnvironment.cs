using Microsoft.Web.WebView2.Core;

namespace UsageBar.Windows.Infrastructure;

internal sealed class WebViewEnvironment : IDisposable
{
    private CoreWebView2Environment? _env;
    private CoreWebView2EnvironmentOptions? _options;
    private Task<CoreWebView2Environment>? _envTask;
    private readonly Lock _lock = new();
    private bool _disposed;

    private static string WebView2DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UsageBar",
        "WebView2");

    public static string AdditionalBrowserArguments =>
        "--disable-gpu --disable-gpu-compositing " +
        "--renderer-process-limit=1 --disable-renderer-backgrounding " +
        "--disable-background-timer-throttling " +
        "--disable-features=Translate,BackForwardCache,MediaRouter,OptimizationHints,AcceptCHFrame " +
        "--disable-accelerated-2d-canvas " +
        "--disable-font-subpixel-positioning";

    public Task<CoreWebView2Environment> GetAsync()
    {
        if (_envTask is not null)
        {
            return _envTask;
        }

        lock (_lock)
        {
            if (_envTask is not null)
            {
                return _envTask;
            }

            _envTask = CreateAsync();
        }
        return _envTask;
    }

    private async Task<CoreWebView2Environment> CreateAsync()
    {
        _options = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = AdditionalBrowserArguments,
        };

        var env = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: WebView2DataDirectory,
            options: _options).ConfigureAwait(true);

        _env = env;
        return env;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_lock)
        {
            (_env as IDisposable)?.Dispose();
            _env = null;
            _envTask = null;
        }
    }
}