using System.Collections.Concurrent;

namespace UsageBar.Shell.Tray;

/// <summary>
/// Minimal SynchronizationContext that marshals delegates to the STA UI thread
/// via a Win32 window message pump. Required for WebView2 async continuations
/// to land on the tray message-loop thread.
/// </summary>
internal sealed class TrayUiSyncContext : SynchronizationContext
{
    private readonly nint _hwnd;
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();

    public TrayUiSyncContext(nint hwnd)
    {
        _hwnd = hwnd;
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        _queue.Enqueue((d, state));
        NativeMethods.PostMessage(_hwnd, NativeMethods.WmRunDelegate, 0, 0);
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (Environment.CurrentManagedThreadId == _managedThreadId)
        {
            d(state);
            return;
        }

        using var done = new ManualResetEventSlim();
        Post(s =>
        {
            try { d(s); }
            finally { ((ManualResetEventSlim)state!).Set(); }
        }, done);
        done.Wait();
    }

    private readonly int _managedThreadId = Environment.CurrentManagedThreadId;

    /// <summary>
    /// Drains all queued callbacks. Call from the UI thread's WndProc when
    /// <see cref="NativeMethods.WmRunDelegate"/> is received.
    /// </summary>
    public void Drain()
    {
        while (_queue.TryDequeue(out var work))
        {
            work.Callback(work.State);
        }
    }
}
