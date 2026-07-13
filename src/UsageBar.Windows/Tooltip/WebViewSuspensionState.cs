namespace UsageBar.Windows.Tooltip;

/// <summary>
/// Tracks the requested visibility separately from the renderer's asynchronous suspension.
/// A resume request can arrive while WebView2 is still completing TrySuspendAsync; when that
/// happens the completed suspension must immediately be undone.
/// </summary>
internal sealed class WebViewSuspensionState
{
    private bool _visibleRequested;
    private bool _suspendPending;

    public bool IsSuspended { get; private set; }

    public bool RequestResume()
    {
        _visibleRequested = true;
        if (!IsSuspended)
        {
            return false;
        }

        IsSuspended = false;
        return true;
    }

    public bool RequestSuspend()
    {
        _visibleRequested = false;
        if (IsSuspended || _suspendPending)
        {
            return false;
        }

        _suspendPending = true;
        return true;
    }

    public bool CompleteSuspend(bool suspended)
    {
        _suspendPending = false;
        if (!suspended)
        {
            return false;
        }

        IsSuspended = true;
        if (!_visibleRequested)
        {
            return false;
        }

        IsSuspended = false;
        return true;
    }
}
