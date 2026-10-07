using System.Runtime.InteropServices;
using PingXu.Core;

namespace PingXu.Windows;

internal sealed class Win32CcdApi : ICcdApi
{
    // Keep query and set in the same legacy CCD mode: full 32-bit ModeIndex, not virtual bitfield indices.
    // QDC_ALL_PATHS supplies inactive routes needed to safely enable attached, disabled outputs.
    private const uint QueryFlags = 1;
    private static void EnsureConsole()
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new InvalidOperationException("屏搭原生显示适配器需要 Windows x64 进程。");
        if (!Environment.UserInteractive || NativeMethods.GetSystemMetrics(0x1000) != 0 ||
            !NativeMethods.ProcessIdToSessionId(NativeMethods.GetCurrentProcessId(), out var session) || session == 0 ||
            session != NativeMethods.WTSGetActiveConsoleSessionId())
            NativeErrors.Check(5, "访问显示配置");
    }
    public CcdState Read()
    {
        EnsureConsole();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var error = NativeMethods.GetDisplayConfigBufferSizes(QueryFlags, out var pathCount, out var modeCount);
            if (error == 122) continue;
            NativeErrors.Check(error, "读取显示配置大小");
            if (pathCount > 65536 || modeCount > 65536) throw new InvalidOperationException("Windows 返回的显示配置数组过大。");
            var paths = new PathInfo[Math.Max(1, pathCount)]; var modes = new ModeInfo[Math.Max(1, modeCount)];
            error = NativeMethods.QueryDisplayConfig(QueryFlags, ref pathCount, paths, ref modeCount, modes, 0);
            if (error == 122) continue;
            NativeErrors.Check(error, "读取显示拓扑");
            if (pathCount > paths.Length || modeCount > modes.Length) throw new InvalidOperationException("Windows 返回的显示配置数组长度异常。");
            Array.Resize(ref paths, (int)pathCount); Array.Resize(ref modes, (int)modeCount);
            // Disconnected inactive connector possibilities are not attached monitor outputs.
            paths = paths.Where(p => p.Target.Available != 0 || CcdLogic.Active(p)).ToArray();
            try { return Enrich(paths, modes); }
            catch (TopologyChangedException) when (attempt < 3) { }
        }
        NativeErrors.Check(122, "读取显示拓扑");
        throw new InvalidOperationException("读取显示拓扑失败。");
    }
    private static CcdState Enrich(PathInfo[] paths, ModeInfo[] modes)
    {
        var activeSources = paths.Where(CcdLogic.Active).Select(CcdLogic.Source).ToHashSet();
        var sourceNames = new Dictionary<Endpoint, string>();
        var modeCache = new Dictionary<string, DisplayMode[]>(StringComparer.OrdinalIgnoreCase);
        var outputs = new List<Output>();
        foreach (var group in paths.GroupBy(CcdLogic.Target))
        {
            var route = group.OrderByDescending(CcdLogic.Active).ThenBy(p => activeSources.Contains(CcdLogic.Source(p))).First();
            var targetName = new TargetDeviceName { Header = Header<TargetDeviceName>(2, group.Key) };
            var error = NativeMethods.GetTargetName(ref targetName);
            if (error is 87 or 1167) throw new TopologyChangedException();
            NativeErrors.Check(error, "读取显示器设备身份");
            var source = CcdLogic.Source(route);
            if (!sourceNames.TryGetValue(source, out var gdi))
            {
                var name = new SourceDeviceName { Header = Header<SourceDeviceName>(1, source) };
                error = NativeMethods.GetSourceName(ref name);
                if (error is 87 or 1167) throw new TopologyChangedException();
                NativeErrors.Check(error, "读取 GDI 显示名称");
                gdi = name.GdiDeviceName ?? ""; sourceNames.Add(source, gdi);
            }
            var preferred = new TargetPreferredMode { Header = Header<TargetPreferredMode>(3, group.Key) };
            var preferredError = NativeMethods.GetPreferredMode(ref preferred);
            if (preferredError == 5) NativeErrors.Check(preferredError, "读取显示器首选模式");
            VideoSignal? signal = preferredError == 0 && preferred.Signal.ActiveSize.Width > 0 && preferred.Signal.ActiveSize.Height > 0
                ? preferred.Signal : null;
            var supported = new List<DisplayMode>();
            // A disabled target can share candidate sources with another live monitor. Do not advertise that monitor's GDI modes.
            if (!string.IsNullOrWhiteSpace(gdi) && (CcdLogic.Active(route) || !activeSources.Contains(source)))
            {
                if (!modeCache.TryGetValue(gdi, out var list)) modeCache.Add(gdi, list = EnumerateModes(gdi));
                supported.AddRange(list);
            }
            if (signal is { } s && DisplayConversion.Hertz(s.VSync) > 0)
                supported.Add(new(checked((int)s.ActiveSize.Width), checked((int)s.ActiveSize.Height), DisplayConversion.Hertz(s.VSync)));
            outputs.Add(new(group.Key, targetName.MonitorDevicePath ?? "", targetName.FriendlyName ?? "", gdi,
                supported.Distinct().OrderByDescending(m => (long)m.Width * m.Height).ThenByDescending(m => m.RefreshRate).ToArray(), signal));
        }
        return new(paths, modes, outputs.ToArray());
    }
    private static DisplayMode[] EnumerateModes(string name)
    {
        var list = new List<DisplayMode>();
        for (uint index = 0; index < 16384; index++)
        {
            var mode = new DevMode { Size = (ushort)Marshal.SizeOf<DevMode>() };
            // No EDS_RAWMODE: keep driver/monitor filtering; no registry/current mode as invented support.
            if (!NativeMethods.EnumDisplaySettingsEx(name, index, ref mode, 0)) return list.Distinct().ToArray();
            if (DisplayConversion.FromDevMode(mode) is { } converted) list.Add(converted);
        }
        throw new InvalidOperationException("显示驱动模式枚举未结束，请重新检测或检查驱动。");
    }
    private static DeviceInfoHeader Header<T>(uint type, Endpoint endpoint) where T : struct => new()
    { Type = type, Size = (uint)Marshal.SizeOf<T>(), AdapterId = endpoint.AdapterId, Id = endpoint.Id };
    public int Set(CcdPlan plan, uint flags)
    {
        EnsureConsole();
        if (plan.Paths.Length == 0 || !plan.Paths.Any(CcdLogic.Active))
            throw new InvalidOperationException("拒绝向 Windows 提交空显示拓扑。");
        if (flags is not (0x60 or 0xA0 or 0x2A0 or 0x460 or 0x4A0 or 0x6A0))
            throw new InvalidOperationException("不支持的显示配置操作标志。");
        return NativeMethods.SetDisplayConfig((uint)plan.Paths.Length, plan.Paths, (uint)plan.Modes.Length, plan.Modes, flags);
    }
    private sealed class TopologyChangedException : InvalidOperationException
    { internal TopologyChangedException() : base("读取设备信息期间显示连接发生变化，请重新检测。") { } }
}
