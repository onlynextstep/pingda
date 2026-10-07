namespace PingXu.Core;
public static class LayoutPlanner
{
    // Compatibility entry point: historical parameter names do not impose a resolution or screen count.
    public static List<DisplayTarget> Arrange(IReadOnlyList<DisplayInfo> displays, string fourK, int rotation, bool onlyFourK, bool disableFourK)
        => ArrangeHorizontal(displays, fourK, rotation, onlyFourK, disableFourK);

    /// <summary>
    /// Explicitly arrange the current active set horizontally around its primary screen.
    /// The selected identity is case-insensitive and must be connected and active: inactive devices lack
    /// an observed desktop mode and are never implicitly enabled. No resolution/Hz is invented.
    /// This is a requested rearrangement, not a capture; use DefaultProfileFactory.Create for exact defaults.
    /// Rotation support and the resulting topology still require native validation before applying.
    /// </summary>
    public static List<DisplayTarget> ArrangeHorizontal(IReadOnlyList<DisplayInfo> displays, string selectedId,
        int rotation, bool onlySelected = false, bool disableSelected = false)
    {
        ArgumentNullException.ThrowIfNull(displays);
        if (string.IsNullOrWhiteSpace(selectedId) || rotation is not (0 or 90 or 180 or 270) || (onlySelected && disableSelected))
            throw new ArgumentException("目标屏幕、方向或启停选项不合法。");
        CheckObserved(displays);
        var selected = displays.SingleOrDefault(d => StringComparer.OrdinalIgnoreCase.Equals(d.Id, selectedId));
        if (selected is not { Connected: true, Enabled: true })
            throw new ArgumentException("所选屏幕没有已启用的实际模式，请先检测并显式设置该屏幕。");
        var ordered = displays.OrderBy(d => d.X).ThenBy(d => d.Y).ThenBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToList();
        var result = ordered.Select(d =>
        {
            bool isSelected = StringComparer.OrdinalIgnoreCase.Equals(d.Id, selectedId);
            var angle = isSelected ? rotation : d.Rotation;
            bool swap = (angle % 180) != (d.Rotation % 180);
            bool enabled = d.Connected && d.Enabled && (onlySelected ? isSelected : !(disableSelected && isSelected));
            return new DisplayTarget(d.Id, enabled, false, d.X, d.Y, swap ? d.Height : d.Width, swap ? d.Width : d.Height, angle, d.RefreshRate);
        }).ToList();
        var main = onlySelected ? selected.Id : ordered.FirstOrDefault(d => d.Primary && result.Any(t => t.Id == d.Id && t.Enabled))?.Id;
        main ??= result.LastOrDefault(t => t.Enabled)?.Id;
        int position = 0, mainPosition = 0;
        for (int i = 0; i < result.Count; i++) { var t = result[i]; if (!t.Enabled) continue; if (t.Id == main) mainPosition = position; result[i] = t with { X = position, Y = 0, Primary = t.Id == main }; position = checked(position + t.Width); }
        result = result.Select(t => t.Enabled ? t with { X = t.X - mainPosition } : t).ToList();
        Check(new("draft", "草稿", result), displays); return result;
    }

    internal static void CheckObserved(IReadOnlyList<DisplayInfo> displays)
    {
        var capacity = DisplayLimits.CheckConnected(displays);
        if (!capacity.Success) throw new ArgumentException(capacity.Message);
        if (displays.Count is 0 or > 32 || displays.Any(d => d is null || string.IsNullOrWhiteSpace(d.Id)) ||
            displays.Select(d => d.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != displays.Count)
            throw new ArgumentException("当前屏幕为空、过多或身份不唯一，不能生成布局。");
        if (displays.Any(d => (d.Enabled && !d.Connected) || (d.Primary && !d.Enabled)))
            throw new ArgumentException("当前连接、启用或主屏状态矛盾，请重新检测。");
        var active = displays.Where(d => d.Enabled).ToArray();
        if (active.Length > 1 && active.Any(a => active.Any(b => a.Id != b.Id && a.X == b.X && a.Y == b.Y)))
            throw new ArgumentException("复制显示或重叠桌面不受支持，请先切换为扩展桌面。");
        Check(new("observed", "当前实际布局", displays.Select(ToTarget).ToList()), displays);
    }

    internal static DisplayTarget ToTarget(DisplayInfo d) => new(d.Id, d.Enabled, d.Primary, d.X, d.Y,
        d.Width, d.Height, d.Rotation, d.RefreshRate);

    /// <summary>Checks the full hardware connection limit, layout shape and required identities, not driver mode support.</summary>
    public static void Check(DisplayProfile profile, IReadOnlyList<DisplayInfo> displays)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(displays);
        var capacity = DisplayLimits.CheckConnected(displays);
        if (!capacity.Success) throw new ArgumentException(capacity.Message);
        CheckProfileShape(profile);
        foreach (var t in profile.Displays.Where(t => t.Enabled))
            if (displays.Count(d => string.Equals(d.Id, t.Id, StringComparison.OrdinalIgnoreCase) && d.Connected) != 1)
                throw new ArgumentException("所需显示器未连接，或身份无法唯一确定。请重新扫描。");
    }

    // Storage/import validation has no evidence of live connections. Keep legacy 32-target compatibility.
    internal static void CheckProfileShape(DisplayProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 60) throw new ArgumentException("预设名称须为1至60个字符。");
        var all = profile.Displays ?? throw new ArgumentException("预设没有屏幕配置。");
        if (all.Count == 0 || all.Count > 32 || all.Any(t => t is null || string.IsNullOrWhiteSpace(t.Id)) || all.Select(t => t.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != all.Count) throw new ArgumentException("屏幕身份重复、为空或配置为空。");
        var active = all.Where(t => t.Enabled).ToList();
        if (active.Count == 0) throw new ArgumentException("至少保留一块显示器开启。");
        if (active.Count(t => t.Primary) != 1) throw new ArgumentException("必须有且只有一个主屏。");
        foreach (var t in active)
        {
            if (t.Width < 320 || t.Height < 320 || t.Width > 32768 || t.Height > 32768 || t.RefreshRate < 1 || t.RefreshRate > 1000 || !new[] { 0, 90, 180, 270 }.Contains(t.Rotation)) throw new ArgumentException("分辨率、刷新率或旋转方向不合法。");
            if (t.Primary && (t.X != 0 || t.Y != 0)) throw new ArgumentException("主屏必须位于布局原点。");
            if (Math.Abs((long)t.X) > 100000 || Math.Abs((long)t.Y) > 100000) throw new ArgumentException("屏幕位置超出范围。");
        }
        for (int i = 0; i < active.Count; i++) for (int j = i + 1; j < active.Count; j++)
            {
                var a = active[i]; var b = active[j];
                if (a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y) throw new ArgumentException("显示器布局存在重叠，请调整位置。");
            }
    }
}
