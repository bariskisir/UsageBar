using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace UsageBar.Windows.Tooltip;

/// <summary>Owns the shared WebView controller visibility and suspend/resume state machine.</summary>
internal sealed class WebViewPopupLifecycle(ILogger logger, string surfaceName) : IDisposable
{
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;
    private readonly WebViewSuspensionState _suspension = new();
    private bool _ready;
    private bool _disposed;

    public bool IsReady => _ready;
    public bool IsSuspended => _suspension.IsSuspended;
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
        if (_suspension.RequestResume())
        {
            ResumeRenderer();
        }
    }

    public async Task SuspendAsync()
    {
        if (_disposed || _controller is null || _core is null || !_ready)
        {
            return;
        }

        _controller.IsVisible = false;
        _core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
        if (!_suspension.RequestSuspend())
        {
            return;
        }

        try
        {
            var suspended = await _core.TrySuspendAsync();
            if (suspended)
            {
                logger.LogDebug("{WebViewSurface} renderer suspended.", surfaceName);
            }

            // Resume may have been requested while TrySuspendAsync was in flight. WebView2
            // can still finish suspending after that request, so immediately reconcile the
            // real renderer state with the latest requested visibility.
            if (_suspension.CompleteSuspend(suspended))
            {
                ResumeRenderer();
            }
        }
        catch (Exception exception)
        {
            _suspension.CompleteSuspend(false);
            logger.LogDebug("{WebViewSurface} suspend skipped: {ExceptionType}.", surfaceName, exception.GetType().Name);
        }
    }

    private void ResumeRenderer()
    {
        try
        {
            _core?.Resume();
            logger.LogDebug("{WebViewSurface} renderer resumed.", surfaceName);
        }
        catch (Exception exception)
        {
            logger.LogDebug("{WebViewSurface} resume skipped during teardown: {ExceptionType}.", surfaceName, exception.GetType().Name);
        }
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
