using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace UsageBar.Windows.Tooltip;

/// <summary>Owns the shared WebView controller visibility lifecycle.</summary>
internal sealed class WebViewPopupLifecycle(ILogger logger, string surfaceName) : IDisposable
{
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;
    private bool _ready;
    private bool _disposed;

    public bool IsReady => _ready;
    public bool IsDisposed => _disposed;

    public void Attach(CoreWebView2Controller controller)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _controller = controller;
        _core = controller.CoreWebView2;
        _controller.IsVisible = false;
        _core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
        logger.LogDebug("{WebViewSurface} controller attached in low-memory mode.", surfaceName);
    }

    public void MarkReady()
    {
        _ready = true;
        logger.LogInformation("{WebViewSurface} document is ready.", surfaceName);
    }

    public void Resume()
    {
        if (_disposed || _controller is null || _core is null)
        {
            return;
        }

        _controller.IsVisible = true;
        _core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
    }

    public Task SuspendAsync()
    {
        if (_disposed || _controller is null || _core is null || !_ready)
        {
            return Task.CompletedTask;
        }

        _controller.IsVisible = false;
        _core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller?.Close();
        (_controller as IDisposable)?.Dispose();
        _controller = null;
        _core = null;
        logger.LogDebug("{WebViewSurface} controller disposed.", surfaceName);
    }
}
