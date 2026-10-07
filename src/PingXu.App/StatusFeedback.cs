namespace PingXu.App;

// UI-thread owned; null hides the notice. Time is injected for deterministic tests.
public sealed class StatusFeedback(Action<string?> render, Func<TimeSpan, Action, IDisposable> schedule) : IDisposable
{
    IDisposable? pending;
    long revision;
    bool disposed;

    public void Show(string message)
    {
        if (disposed) return;
        Cancel();
        render(message);
    }

    public void ShowSuccess(string message)
    {
        if (disposed) return;
        Show(message);
        var current = revision;
        pending = schedule(TimeSpan.FromSeconds(4), () =>
        {
            if (disposed || current != revision) return;
            Cancel();
            render(null);
        });
    }

    void Cancel()
    {
        revision++;
        pending?.Dispose();
        pending = null;
    }

    public void Dispose()
    {
        disposed = true;
        Cancel();
    }
}
