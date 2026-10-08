using System.Runtime.InteropServices;

namespace PingXu.App;

public static class FatalProcessExit
{
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateProcess(IntPtr process, uint code);

    // Do not run ProcessExit/SystemEvents cleanup on a broken render dispatcher:
    // SystemEvents can be synchronously waiting for that same dispatcher.
    // Only this UI process is terminated; display recovery is a separate process.
    public static void Terminate(int code)
    {
        if (!TerminateProcess(GetCurrentProcess(), (uint)code))
            Environment.FailFast("无法结束故障界面进程。");
    }
}

public sealed class InstanceActivation : IDisposable
{
    readonly EventWaitHandle request;
    readonly ManualResetEvent stop = new(false);
    readonly Thread listener;
    public static string Name => "Local\\PingDa.Activate." + Environment.UserName;

    public InstanceActivation(string name, Action activate)
    {
        request = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        listener = new Thread(() =>
        {
            while (WaitHandle.WaitAny([request, stop]) == 0)
            {
                try { activate(); }
                catch (Exception ex) { Program.Log(ex); }
            }
        }) { IsBackground = true, Name = "PingDa activation" };
        listener.Start();
    }

    public static bool TryActivate(string name)
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(name, out var handle)) return false;
            using (handle) return handle.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
    }

    public void Dispose()
    {
        stop.Set();
        if (listener.Join(TimeSpan.FromSeconds(1))) { request.Dispose(); stop.Dispose(); }
    }
}
