using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace UsageBar.Windows.Tooltip;

/// <summary>Owns the shared WebView controller visibility and suspend/resume state machine.</summary>
internal sealed class WebViewPopupLifecycle(ILogger logger, string surfaceName) : IDisposable
{
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;
    private bool _ready;
    private bool _suspended;
    private bool _disposed;
    private int _suspendVersion;

    public bool IsReady => _ready;
    public bool IsSuspended => _suspended;
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
        Interlocked.Increment(ref _suspendVersion);
        if (_disposed || _controller is null || _core is null)
        {
            return;
        }

        _controller.IsVisible = true;
        _core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
        if (_suspended)
        {
            _suspended = false;
            try
            {
                _core.Resume();
                logger.LogDebug("{WebViewSurface} renderer resumed.", surfaceName);
            }
            catch (Exception exception)
            {
                logger.LogDebug("{WebViewSurface} resume skipped during teardown: {ExceptionType}.", surfaceName, exception.GetType().Name);
            }
        }
    }

    public async Task SuspendAsync()
    {
        if (_disposed || _controller is null || _core is null || !_ready || _suspended)
        {
            return;
        }

        _controller.IsVisible = false;
        _core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
        var version = _suspendVersion;

        try
        {
            var suspended = await _core.TrySuspendAsync();
            if (suspended && version == _suspendVersion)
            {
                _suspended = true;
                logger.LogDebug("{WebViewSurface} renderer suspended.", surfaceName);
            }
        }
        catch (Exception exception)
        {
            logger.LogDebug("{WebViewSurface} suspend skipped: {ExceptionType}.", surfaceName, exception.GetType().Name);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Interlocked.Increment(ref _suspendVersion);
        _controller?.Close();
        (_controller as IDisposable)?.Dispose();
        _controller = null;
        _core = null;
        logger.LogDebug("{WebViewSurface} controller disposed.", surfaceName);
    }
}