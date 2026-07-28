using Gtk;

namespace UsageBar.Linux.Infrastructure;

internal sealed class GtkDispatcher
{
    private readonly int _mainThreadId = Environment.CurrentManagedThreadId;

    public void Invoke(System.Action action)
    {
        if (Environment.CurrentManagedThreadId == _mainThreadId)
        {
            action();
            return;
        }

        Application.Invoke((_, _) => action());
    }
}
