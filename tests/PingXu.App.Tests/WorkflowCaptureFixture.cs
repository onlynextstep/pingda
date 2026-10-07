using System.Collections.Concurrent;
using PingXu.Core;

namespace PingXu.App.Tests;

// Opt-in service; the default UI fixture still rejects every service call.
internal sealed class WorkflowDisplayService : IDisplayService
{
    internal DesktopSnapshot Live { get; set; } = FakeDisplayService.Snapshot();
    internal Func<DesktopSnapshot>? OnCapture { get; set; }
    internal int Captures;
    internal int UnexpectedCalls;
    public DesktopSnapshot Capture()
    {
        Interlocked.Increment(ref Captures);
        return OnCapture?.Invoke() ?? Live;
    }
    private OperationResult Unexpected()
    {
        Interlocked.Increment(ref UnexpectedCalls);
        throw new InvalidOperationException("假服务禁止 Validate/Apply/Restore");
    }
    public OperationResult Validate(DisplayProfile profile) => Unexpected();
    public OperationResult Apply(DisplayProfile profile, bool persist) => Unexpected();
    public OperationResult Restore(DesktopSnapshot snapshot) => Unexpected();
}

// Only test-owned callbacks run on the existing STA. No WPF/native message loop.
internal sealed class WorkflowCallbacks : SynchronizationContext, IDisposable
{
    private readonly SynchronizationContext? previous = Current;
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> callbacks = new();
    private int operations;
    internal WorkflowCallbacks() => SetSynchronizationContext(this);
    public override void Post(SendOrPostCallback d, object? state) => callbacks.Add((d, state));
    public override void OperationStarted() => Interlocked.Increment(ref operations);
    public override void OperationCompleted() => Interlocked.Decrement(ref operations);
    internal void Complete()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Volatile.Read(ref operations) != 0 || callbacks.Count != 0)
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("假硬件异步事件未完成");
            if (callbacks.TryTake(out var next, 100)) next.Callback(next.State);
        }
    }
    public void Dispose()
    {
        SetSynchronizationContext(previous);
        callbacks.Dispose();
    }
}
