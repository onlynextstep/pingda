using PingXu.Core;

namespace PingXu.Windows;

internal static class CcdLogic
{
    internal const uint InvalidIndex = uint.MaxValue;
    internal static readonly StringComparer IdentityComparer = StringComparer.OrdinalIgnoreCase;
    internal static Endpoint Source(PathInfo p) => new(p.Source.AdapterId, p.Source.Id);
    internal static Endpoint Target(PathInfo p) => new(p.Target.AdapterId, p.Target.Id);
    internal static bool Active(PathInfo p) => (p.Flags & 1) != 0;
    internal static ModeInfo Mode(CcdState state, PathInfo path, bool source)
    {
        var index = source ? path.Source.ModeIndex : path.Target.ModeIndex;
        if (index >= state.Modes.Length) throw new InvalidOperationException("Windows 显示模式索引不可用，无法安全保存或恢复。请重新检测。");
        var mode = state.Modes[index]; var endpoint = source ? Source(path) : Target(path);
        if (mode.Type != (source ? 1u : 2u) || mode.Id != endpoint.Id || !mode.AdapterId.Equals(endpoint.AdapterId))
            throw new InvalidOperationException("Windows 显示模式与路径不匹配，请重新检测。");
        return mode;
    }

    internal static List<DisplayInfo> Describe(CcdState state)
    {
        if (state.Paths.Length == 0 || !state.Paths.Any(Active))
            throw new InvalidOperationException("未检测到可用的活动显示输出。");
        var active = state.Paths.Where(Active).ToArray();
        if (active.Any(p => (p.Flags & ~1u) != 0))
            throw new InvalidOperationException("当前显示路径包含不支持的虚拟模式或动态刷新标志，无法保证恢复，请使用普通扩展桌面。");
        if (active.GroupBy(Source).Any(g => g.Count() > 1))
            throw new InvalidOperationException("当前为复制显示拓扑，屏搭仅支持本地扩展桌面。请先在 Windows 设置中切换为扩展。");
        if (active.GroupBy(Target).Any(g => g.Count() > 1) || state.Outputs.GroupBy(o => o.Target).Any(g => g.Count() > 1))
            throw new InvalidOperationException("显示输出标识重复，无法确定拓扑。");
        foreach (var p in active)
        {
            _ = Mode(state, p, true); _ = Mode(state, p, false);
            if (!state.Outputs.Any(o => o.Target == Target(p))) throw new InvalidOperationException("活动显示输出缺少设备身份。");
        }
        var displays = new List<DisplayInfo>();
        foreach (var output in state.Outputs)
        {
            var routes = state.Paths.Where(p => Target(p) == output.Target).ToArray();
            if (routes.Length == 0) throw new InvalidOperationException("显示输出缺少路径。");
            var enabled = routes.Any(Active);
            var path = routes.FirstOrDefault(Active, routes[0]);
            var connected = routes.Any(p => p.Target.Available != 0);
            var rotation = enabled ? DisplayConversion.Degrees(path.Target.Rotation) : 0;
            var src = enabled ? Mode(state, path, true).Data.Source : default;
            // CCD source dimensions are unrotated; Core exposes the rotated desktop footprint.
            var size = enabled ? DisplayConversion.DesktopSize(checked((int)src.Width), checked((int)src.Height), rotation)
                : (output.PreferredSignal is { } preferred ? (checked((int)preferred.ActiveSize.Width), checked((int)preferred.ActiveSize.Height)) : (0, 0));
            var refresh = enabled ? DisplayConversion.Hertz(path.Target.RefreshRate)
                : output.PreferredSignal is { } signal ? DisplayConversion.Hertz(signal.VSync) : 0;
            if (enabled && refresh == 0) refresh = DisplayConversion.Hertz(Mode(state, path, false).Data.Target.VSync);
            var modes = output.Modes.Where(m => m.Width > 0 && m.Height > 0 && m.RefreshRate > 0).ToList();
            // Preferred timing belongs to this physical target even when its inactive GDI source has no modes.
            if (PreferredMode(output.PreferredSignal) is { } preferredMode)
            {
                modes.Remove(preferredMode);
                modes.Insert(0, preferredMode);
            }
            if (enabled)
            {
                modes.Add(new(checked((int)src.Width), checked((int)src.Height), refresh));
            }
            displays.Add(new(output.Id, output.DeviceName, string.IsNullOrWhiteSpace(output.Name) ? "显示器（未提供名称）" : output.Name,
                connected, enabled, enabled && src.Position.X == 0 && src.Position.Y == 0,
                src.Position.X, src.Position.Y, size.Item1, size.Item2, rotation, refresh, modes.Distinct().ToArray()));
        }
        if (displays.Count(d => d.Enabled && d.Primary) != 1)
            throw new InvalidOperationException("无法唯一确定当前主屏，当前拓扑不受支持。");
        return displays;
    }

    internal static DisplayMode? PreferredMode(VideoSignal? signal)
    {
        if (signal is not { } s || s.ActiveSize.Width == 0 || s.ActiveSize.Height == 0 ||
            s.ActiveSize.Width > int.MaxValue || s.ActiveSize.Height > int.MaxValue || s.VSync.Denominator == 0 ||
            s.ScanLineOrdering is 2 or 3) return null;
        var hz = DisplayConversion.Hertz(s.VSync);
        return hz > 0 ? new((int)s.ActiveSize.Width, (int)s.ActiveSize.Height, hz) : null;
    }

    private static Dictionary<string, Output> UniqueOutputs(CcdState state)
    {
        if (state.Outputs.Any(o => string.IsNullOrWhiteSpace(o.Id)) || state.Outputs.GroupBy(o => o.Id, IdentityComparer).Any(g => g.Count() > 1))
            throw new InvalidOperationException("显示器设备身份缺失或存在歧义，请重新连接并识别显示器后再应用。");
        return state.Outputs.ToDictionary(o => o.Id, IdentityComparer);
    }

    internal static CcdPlan Build(CcdState state, DisplayProfile profile)
    {
        var displays = Describe(state);
        var outputs = UniqueOutputs(state);
        if (profile?.Displays is not { Count: > 0 } requested || requested.Any(t => t is null))
            throw new InvalidOperationException("显示方案不能为空。");
        if (requested.Any(t => string.IsNullOrWhiteSpace(t.Id)) || requested.GroupBy(t => t.Id, IdentityComparer).Any(g => g.Count() > 1))
            throw new InvalidOperationException("方案中的显示器身份为空或重复。");
        // Presets may retain physically absent monitors that are intentionally disabled.
        // Required outputs (including any primary marker) must still resolve; never degrade an enabled set.
        foreach (var target in requested.Where(t => t.Enabled || t.Primary))
            if (!outputs.ContainsKey(target.Id) || !displays.Single(d => IdentityComparer.Equals(d.Id, target.Id)).Connected)
                throw new InvalidOperationException($"显示器不存在或已断开：{target.Id}。请重新检测。");
        var enabled = requested.Where(t => t.Enabled).OrderByDescending(t => t.Primary).ToArray();
        if (enabled.Length == 0) throw new InvalidOperationException("至少需要启用一台显示器，不能关闭全部输出。");
        if (enabled.Count(t => t.Primary) != 1 || requested.Any(t => t.Primary && !t.Enabled))
            throw new InvalidOperationException("必须且只能指定一台已启用显示器为主屏。");
        if (enabled[0].X != 0 || enabled[0].Y != 0) throw new InvalidOperationException("主屏桌面坐标必须为 (0, 0)。");
        foreach (var t in enabled)
        {
            var natural = DisplayConversion.DesktopSize(t.Width, t.Height, t.Rotation);
            if (t.Width <= 0 || t.Height <= 0 || t.RefreshRate <= 0 ||
                !displays.Single(d => IdentityComparer.Equals(d.Id, t.Id)).Modes.Contains(new(natural.Width, natural.Height, t.RefreshRate)))
                throw new InvalidOperationException($"显示器 {t.Id} 不支持所选分辨率或刷新率。");
            if ((long)t.X + t.Width > int.MaxValue || (long)t.Y + t.Height > int.MaxValue)
                throw new InvalidOperationException("显示器桌面坐标超出有效范围。");
        }
        for (var i = 0; i < enabled.Length; i++)
            for (var j = i + 1; j < enabled.Length; j++)
            {
                var a = enabled[i]; var b = enabled[j];
                if (a.X < (long)b.X + b.Width && b.X < (long)a.X + a.Width && a.Y < (long)b.Y + b.Height && b.Y < (long)a.Y + a.Height)
                    throw new InvalidOperationException("扩展桌面的显示器区域不能重叠。");
            }
        var selected = Allocate(state, enabled.Select(t => outputs[t.Id]).ToArray());
        var paths = new List<PathInfo>(); var modes = new List<ModeInfo>();
        for (var i = 0; i < enabled.Length; i++)
        {
            var t = enabled[i]; var path = selected[i];
            var old = state.Paths.FirstOrDefault(p => Active(p) && Target(p) == Target(path));
            var oldActive = Active(old);
            if (oldActive)
            {
                // A different source route still feeds the same physical output; keep its scaling policy.
                path.Target.Scaling = old.Target.Scaling;
            }
            var src = oldActive ? Mode(state, old, true).Data.Source : new SourceMode { PixelFormat = 4 };
            var oldSize = oldActive ? (checked((int)src.Width), checked((int)src.Height)) : (0, 0);
            var natural = DisplayConversion.DesktopSize(t.Width, t.Height, t.Rotation);
            src.Width = (uint)natural.Width; src.Height = (uint)natural.Height; src.Position = new() { X = t.X, Y = t.Y };
            path.Flags = 1; path.Source.StatusFlags = 0; path.Target.StatusFlags = 0;
            path.Source.ModeIndex = (uint)modes.Count;
            modes.Add(new() { Type = 1, Id = path.Source.Id, AdapterId = path.Source.AdapterId, Data = new() { Source = src } });
            path.Target.ModeIndex = InvalidIndex;
            path.Target.Rotation = DisplayConversion.Rotation(t.Rotation);
            // Target timing stays untouched for rotation/position/primary-only changes.
            var oldRefresh = oldActive ? DisplayConversion.Hertz(old.Target.RefreshRate) : 0;
            if (oldActive && oldRefresh == 0) oldRefresh = DisplayConversion.Hertz(Mode(state, old, false).Data.Target.VSync);
            if (oldActive && oldSize == natural && oldRefresh == t.RefreshRate)
            {
                var mode = Mode(state, old, false);
                mode.Id = path.Target.Id; mode.AdapterId = path.Target.AdapterId;
                path.Target.ModeIndex = (uint)modes.Count; modes.Add(mode);
                path.Target.RefreshRate = old.Target.RefreshRate;
                path.Target.ScanLineOrdering = old.Target.ScanLineOrdering;
            }
            else if (!oldActive && outputs[t.Id].PreferredSignal is { } preferred &&
                PreferredMode(preferred) == new DisplayMode(natural.Width, natural.Height, t.RefreshRate) &&
                preferred.PixelRate > 0 && preferred.HSync.Numerator > 0 && preferred.HSync.Denominator > 0 &&
                preferred.TotalSize.Width >= preferred.ActiveSize.Width && preferred.TotalSize.Height >= preferred.ActiveSize.Height)
            {
                // Use only this target's measured preferred timing, never the candidate source's other monitor.
                path.Target.ModeIndex = (uint)modes.Count;
                modes.Add(new() { Type = 2, Id = path.Target.Id, AdapterId = path.Target.AdapterId, Data = new() { Target = preferred } });
                path.Target.RefreshRate = preferred.VSync;
                path.Target.ScanLineOrdering = preferred.ScanLineOrdering;
                if (path.Target.Scaling == 0) path.Target.Scaling = 128;
            }
            else
            {
                // Ask CCD's best-mode logic to resolve a NEW timing; never fabricate a pixel clock.
                path.Target.RefreshRate = new() { Numerator = (uint)t.RefreshRate, Denominator = 1 };
                // Advertised selectable modes exclude interlacing. An explicit refresh request
                // needs progressive scanning; UNSPECIFIED can reject reenabled high-Hz outputs (87).
                path.Target.ScanLineOrdering = 1; // DISPLAYCONFIG_SCANLINE_ORDERING_PROGRESSIVE
                if (path.Target.Scaling == 0) path.Target.Scaling = 128; // DISPLAYCONFIG_SCALING_PREFERRED
            }
            paths.Add(path);
        }
        return new(paths.ToArray(), modes.ToArray());
    }

    // Bipartite matching avoids assigning the same source to two targets (accidental clone).
    private static PathInfo[] Allocate(CcdState state, Output[] outputs)
    {
        var candidates = outputs.Select(o => state.Paths.Where(p => Target(p) == o.Target && p.Target.Available != 0)
            .OrderByDescending(Active).ToArray()).ToArray();
        var owners = new Dictionary<Endpoint, int>(); var selected = new PathInfo[outputs.Length];
        bool Assign(int i, HashSet<Endpoint> seen)
        {
            // Exhaust free routes before an augmenting path moves another output.
            // Inactive targets often list a live source before their own free source.
            foreach (var path in candidates[i])
            {
                var source = Source(path);
                if (seen.Contains(source) || owners.ContainsKey(source)) continue;
                seen.Add(source); owners[source] = i; selected[i] = path; return true;
            }
            foreach (var path in candidates[i])
            {
                var source = Source(path);
                if (!seen.Add(source)) continue;
                if (!owners.TryGetValue(source, out var owner) || Assign(owner, seen))
                { owners[source] = i; selected[i] = path; return true; }
            }
            return false;
        }
        // Path order still follows the requested primary priority. Allocation order first protects
        // retained active routes, including when the new primary is currently disabled.
        foreach (var i in Enumerable.Range(0, outputs.Length).OrderByDescending(i => candidates[i].Any(Active)))
            if (!Assign(i, [])) throw new InvalidOperationException($"显示器 {outputs[i].Id} 没有可用的独立扩展路径，无法启用或恢复原拓扑。");
        return selected;
    }

    internal static CcdPlan Rebind(CcdState saved, CcdState current)
    {
        _ = Describe(saved); _ = Describe(current);
        var oldOutputs = UniqueOutputs(saved); var newOutputs = UniqueOutputs(current);
        var active = saved.Paths.Where(Active).ToArray();
        var targets = active.Select(p => oldOutputs.Values.Single(o => o.Target == Target(p))).ToArray();
        var rebound = targets.Select(o => newOutputs.TryGetValue(o.Id, out var now) ? now
            : throw new InvalidOperationException($"原拓扑显示器不可用：{o.Id}。无法完整恢复。")).ToArray();
        var selected = Allocate(current, rebound); var modes = new List<ModeInfo>();
        for (var i = 0; i < active.Length; i++)
        {
            var path = active[i]; var route = selected[i];
            path.Source.AdapterId = route.Source.AdapterId; path.Source.Id = route.Source.Id;
            path.Target.AdapterId = route.Target.AdapterId; path.Target.Id = route.Target.Id;
            if (path.Target.OutputTechnology != route.Target.OutputTechnology)
                throw new InvalidOperationException("显示器连接类型已改变，无法安全恢复原信号时序。");
            path.Target.Available = route.Target.Available;
            var src = Mode(saved, active[i], true); src.Id = path.Source.Id; src.AdapterId = path.Source.AdapterId;
            var dst = Mode(saved, active[i], false); dst.Id = path.Target.Id; dst.AdapterId = path.Target.AdapterId;
            path.Source.ModeIndex = (uint)modes.Count; modes.Add(src);
            path.Target.ModeIndex = (uint)modes.Count; modes.Add(dst);
            selected[i] = path;
        }
        return new(selected, modes.ToArray());
    }
}
