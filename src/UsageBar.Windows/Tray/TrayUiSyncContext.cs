using System.Collections.Concurrent;

namespace UsageBar.Windows.Tray;

/// <summary>
/// Minimal <see cref="SynchronizationContext"/> that marshals delegates to the STA UI
/// thread via a Win32 window message pump. Required for WebView2 async continuations to
/// land on the tray message-loop thread.
/// </summary>
internal sealed class TrayUiSyncContext : SynchronizationContext
{
    private readonly nint _hwnd;
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly int _managedThreadId = Environment.CurrentManagedThreadId;

    public TrayUiSyncContext(nint hwnd) => _hwnd = hwnd;

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
        Post(
            _ =>
            {
                try
                {
                    d(state);
                }
                finally
                {
                    done.Set();
                }
            },
            state);
        done.Wait();
    }

    /// <summary>
    /// Drains the current thread's <see cref="TrayUiSyncContext"/>, if one is installed. Call
    /// from a UI-thread WndProc when <see cref="NativeMethods.WmRunDelegate"/> is received.
    /// </summary>
    public static void DrainCurrent()
    {
        if (Current is TrayUiSyncContext context)
        {
            context.Drain();
        }
    }

    private void Drain()
    {
        while (_queue.TryDequeue(out var work))
        {
            work.Callback(work.State);
        }
    }
}
