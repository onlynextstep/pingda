using System.Runtime.InteropServices;
using System.Windows;

namespace PingXu.App;

internal static class WindowBounds
{
    // WM_GETMINMAXINFO positions are monitor-relative physical pixels, not WPF DIPs.
    public static Rect Calculate(Rect monitor, Rect work) =>
        new(work.Left - monitor.Left, work.Top - monitor.Top, work.Width, work.Height);

    public static bool Apply(IntPtr window, IntPtr data)
    {
        if (data == IntPtr.Zero) return false;
        var monitor = MonitorFromWindow(window, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return false;
        var bounds = Calculate(info.Monitor.ToRect(), info.Work.ToRect());
        var limits = Marshal.PtrToStructure<MinMaxInfo>(data);
        limits.MaxPosition = new() { X = (int)bounds.X, Y = (int)bounds.Y };
        limits.MaxSize = new() { X = (int)bounds.Width, Y = (int)bounds.Height };
        // Keep Windows/WPF min/max tracking limits intact; these govern manual resizing.
        Marshal.StructureToPtr(limits, data, false);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
    }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] struct MinMaxInfo
    {
        public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
