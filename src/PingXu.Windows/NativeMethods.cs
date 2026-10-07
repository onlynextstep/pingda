using System.Runtime.InteropServices;

namespace PingXu.Windows;

internal static class NativeMethods
{
    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);
    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths,
        ref uint modeCount, [Out] ModeInfo[] modes, nint topologyId);
    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern int SetDisplayConfig(uint pathCount, [In] PathInfo[] paths,
        uint modeCount, [In] ModeInfo[] modes, uint flags);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", ExactSpelling = true, CharSet = CharSet.Unicode)]
    internal static extern int GetTargetName(ref TargetDeviceName request);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", ExactSpelling = true, CharSet = CharSet.Unicode)]
    internal static extern int GetSourceName(ref SourceDeviceName request);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo", ExactSpelling = true)]
    internal static extern int GetPreferredMode(ref TargetPreferredMode request);
    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsExW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplaySettingsEx(string deviceName, uint modeIndex, ref DevMode mode, uint flags);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    internal static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("kernel32.dll", ExactSpelling = true)]
    internal static extern uint GetCurrentProcessId();
    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern int GetSystemMetrics(int index);
}
