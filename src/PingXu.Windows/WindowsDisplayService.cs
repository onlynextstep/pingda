using PingXu.Core;

namespace PingXu.Windows;

public sealed class WindowsDisplayService : IDisplayService, IStableDisplayVerification
{
    private readonly ICcdApi api;
    private readonly Action<int> wait;
    private readonly Action<string>? diagnostic;
    /// <summary>Creates a Windows x64 CCD adapter. Construction does not query or mutate displays.</summary>
    public WindowsDisplayService() : this(new Win32CcdApi()) { }
    public WindowsDisplayService(Action<string> diagnostic) : this(new Win32CcdApi(), diagnostic: diagnostic) { }
    internal WindowsDisplayService(ICcdApi api, Action<int>? wait = null, Action<string>? diagnostic = null)
    { this.api = api; this.wait = wait ?? Thread.Sleep; this.diagnostic = diagnostic; }
    private readonly object gate = new();
    public DesktopSnapshot Capture()
    {
        lock (gate)
        {
            var state = api.Read();
            return new(CcdLogic.Describe(state), SnapshotCodec.Encode(state));
        }
    }
    public OperationResult Validate(DisplayProfile profile) => Run(() =>
    {
        var (_, solver) = Prepare(OwnRequest(profile));
        return OperationResult.Ok(solver
            ? "Windows 显示兼容验证通过；尚未切换显示，应用后仍须严格回读确认请求一致。"
            : "Windows 配置验证通过；尚未切换显示。");
    });
    public OperationResult Apply(DisplayProfile profile, bool persist) => Run(() =>
    {
        // Own the caller's mutable List for the duration of planning and comparison.
        var requested = OwnRequest(profile);
        var (plan, solver) = Prepare(requested);
        NativeErrors.Check(api.Set(CopyPlan(plan), ApplyFlags(persist, solver)), "应用显示方案");
        var verification = VerifyStable(requested, persist ? "persist" : "apply");
        if (!verification.Success) return verification;
        return OperationResult.Ok(persist ? "显示方案已保存，Windows 回读的请求状态一致。" : "临时显示方案已应用，Windows 回读的请求状态一致。");
    });
    public OperationResult Verify(DisplayProfile profile) => Run(() => VerifyStable(OwnRequest(profile), "before-save"));

    private OperationResult VerifyStable(DisplayProfile requested, string phase)
    {
        // Read only: never repeat SetDisplayConfig during settling. Require consecutive matches.
        // Thirteen reads, 250ms apart, bound our extra wait to three seconds (native call time excluded).
        var samples = new List<object>();
        string? capacityError = null;
        int consecutive = 0;
        for (int attempt = 0; attempt < 13; attempt++)
        {
            List<DisplayInfo>? actual = null;
            capacityError = null;
            try
            {
                actual = CcdLogic.Describe(api.Read());
                var capacity = DisplayLimits.CheckConnected(actual);
                if (!capacity.Success) { capacityError = capacity.Message; throw new InvalidOperationException(capacity.Message); }
                Compare(requested.Displays.Where(d => d.Enabled), actual);
                samples.Add(new { Attempt = attempt + 1, Match = true });
                if (++consecutive == 2) return OperationResult.Ok();
            }
            catch (Exception e) when (Expected(e))
            {
                consecutive = 0;
                samples.Add(new { Attempt = attempt + 1, Match = false, Error = e.Message,
                    Actual = actual?.Select(d => new { d.Id, d.DeviceName, d.Connected, d.Enabled,
                        d.Primary, d.X, d.Y, d.Width, d.Height, d.Rotation, d.RefreshRate }).ToArray() });
            }
            if (attempt < 12) wait(250);
        }
        // Diagnostic storage must never prevent rollback. Device identifiers stay in local logs, not dialogs.
        try { diagnostic?.Invoke(System.Text.Json.JsonSerializer.Serialize(new {
            Event = "DisplayReadbackUnstable", Phase = phase, Requested = requested.Displays, Samples = samples })); }
        catch { }
        return OperationResult.Fail(capacityError ?? "未能确认所有屏幕已按预设启用。");
    }
    public OperationResult Restore(DesktopSnapshot snapshot) => Restore(snapshot, false);
    /// <summary>
    /// Restores the exact native snapshot after rebinding current device identities.
    /// Set persist to true to also replace the saved display configuration after a failed persistent apply.
    /// Success requires native validation, application and matching desktop/signal readback.
    /// </summary>
    public OperationResult Restore(DesktopSnapshot snapshot, bool persist) => Run(() =>
    {
        if (snapshot is null) return OperationResult.Fail("原生显示快照不能为空。");
        var saved = SnapshotCodec.Decode(snapshot.NativeData);
        var current = api.Read();
        var plan = CcdLogic.Rebind(saved, current);
        var solver = ValidatePlan(plan, current, "验证原显示拓扑");
        NativeErrors.Check(api.Set(CopyPlan(plan), ApplyFlags(persist, solver)), persist ? "恢复并保存原显示拓扑" : "恢复原显示拓扑");
        try
        {
            var actual = api.Read();
            Compare(CcdLogic.Describe(saved).Where(d => d.Enabled).Select(d => new DisplayTarget(d.Id, true, d.Primary,
                d.X, d.Y, d.Width, d.Height, d.Rotation, d.RefreshRate)), CcdLogic.Describe(actual));
            CompareNative(saved, actual);
        }
        catch (Exception e) when (Expected(e))
        { return OperationResult.Fail($"已尝试恢复，但回读未确认原拓扑和信号一致：{e.Message} 请在 Windows 显示设置中检查。"); }
        return OperationResult.Ok(persist
            ? "原显示拓扑已恢复并保存，Windows 回读的布局与原生信号一致。"
            : "原显示拓扑已临时恢复，Windows 回读的布局与原生信号一致。");
    });
    private static DisplayProfile OwnRequest(DisplayProfile profile)
    {
        if (profile?.Displays is null) throw new InvalidOperationException("显示方案不能为空。");
        return profile with { Displays = profile.Displays.ToList() };
    }
    private (CcdPlan Plan, bool Solver) Prepare(DisplayProfile requested)
    {
        var current = api.Read();
        var capacity = DisplayLimits.CheckConnected(CcdLogic.Describe(current));
        if (!capacity.Success) throw new InvalidOperationException(capacity.Message);
        var plan = CcdLogic.Build(current, requested);
        return (plan, ValidatePlan(plan, current, "验证显示方案"));
    }
    private bool ValidatePlan(CcdPlan plan, CcdState current, string action)
    {
        // Compare the same physical targets after Build/Rebind, not source IDs or an assumed 0 degrees.
        // Only an active current path supplies a measured orientation for the narrow compatibility gate.
        var rotationChanged = plan.Paths.Any(p => CcdLogic.Active(p) && current.Paths.Any(old =>
            CcdLogic.Active(old) && CcdLogic.Target(old) == CcdLogic.Target(p) && old.Target.Rotation != p.Target.Rotation));
        var enabling = plan.Paths.Any(p => CcdLogic.Active(p) &&
            current.Paths.Any(old => old.Target.Available != 0 && CcdLogic.Target(old) == CcdLogic.Target(p)) &&
            !current.Paths.Any(old => CcdLogic.Active(old) && CcdLogic.Target(old) == CcdLogic.Target(p)));
        var error = api.Set(CopyPlan(plan), 0x60);
        if (error == 0) return false;
        if (error is not (31 or 1610) || !(rotationChanged || enabling))
            NativeErrors.Check(error, action);
        // Windows may solve source/target details. Never replace the caller/snapshot contract with its solution.
        // Apply must compare every requested desktop field; Restore must additionally compare exact native data.
        NativeErrors.Check(api.Set(CopyPlan(plan), 0x460), action + (enabling ? "（重新启用兼容）" : "（旋转兼容）"));
        return true;
    }
    private static CcdPlan CopyPlan(CcdPlan plan) => new(plan.Paths.ToArray(), plan.Modes.ToArray());
    private static uint ApplyFlags(bool persist, bool solver) => (persist ? 0x2A0u : 0xA0u) | (solver ? 0x400u : 0u);
    private OperationResult Run(Func<OperationResult> action)
    {
        lock (gate)
        {
            try { return action(); }
            catch (Exception e) when (Expected(e)) { return OperationResult.Fail(e.Message); }
        }
    }
    private static bool Expected(Exception e) => e is InvalidOperationException or ArgumentException or OverflowException;
    private static void Compare(IEnumerable<DisplayTarget> requested, List<DisplayInfo> actual)
    {
        var want = requested.ToArray(); var on = actual.Where(d => d.Enabled).ToArray();
        if (on.Length != want.Length) throw new InvalidOperationException("启用的显示器数量不同。");
        foreach (var t in want)
        {
            var matches = on.Where(d => CcdLogic.IdentityComparer.Equals(d.Id, t.Id)).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException($"显示器身份不一致：{t.Id}。");
            var d = matches[0];
            if (!d.Connected || d.Primary != t.Primary || d.X != t.X || d.Y != t.Y || d.Width != t.Width ||
                d.Height != t.Height || d.Rotation != t.Rotation || d.RefreshRate != t.RefreshRate)
                throw new InvalidOperationException($"显示器 {d.Name} 的状态与请求不同。请求 {t.Width}×{t.Height} / {t.Rotation}° / {t.RefreshRate}Hz / ({t.X},{t.Y}) / 主屏={t.Primary}；实际 {d.Width}×{d.Height} / {d.Rotation}° / {d.RefreshRate}Hz / ({d.X},{d.Y}) / 主屏={d.Primary}。");
        }
    }
    private static void CompareNative(CcdState saved, CcdState actual)
    {
        foreach (var old in saved.Paths.Where(CcdLogic.Active))
        {
            var id = saved.Outputs.Single(o => o.Target == CcdLogic.Target(old)).Id;
            var output = actual.Outputs.Single(o => CcdLogic.IdentityComparer.Equals(o.Id, id));
            var now = actual.Paths.Single(p => CcdLogic.Active(p) && CcdLogic.Target(p) == output.Target);
            var a = CcdLogic.Mode(saved, old, false).Data.Target;
            var b = CcdLogic.Mode(actual, now, false).Data.Target;
            if (!SnapshotCodec.Bytes(new[] { a }).SequenceEqual(SnapshotCodec.Bytes(new[] { b })) ||
                old.Target.Scaling != now.Target.Scaling || old.Target.OutputTechnology != now.Target.OutputTechnology ||
                old.Target.ScanLineOrdering != now.Target.ScanLineOrdering || !old.Target.RefreshRate.Equals(now.Target.RefreshRate) ||
                CcdLogic.Mode(saved, old, true).Data.Source.PixelFormat != CcdLogic.Mode(actual, now, true).Data.Source.PixelFormat)
                throw new InvalidOperationException($"显示器 {id} 的原生信号、缩放方式或像素格式未精确恢复。");
        }
    }
}
