namespace PingXu.Core;

/// <summary>One reversible trial. The guardian owns the clock and this transaction owns mutation order.</summary>
public sealed class DisplayTransaction(IDisplayService service,
    Func<DesktopSnapshot, bool, OperationResult> restore,
    Action<DesktopSnapshot> journal,
    Action<DesktopSnapshot> savePrevious)
{
    DesktopSnapshot? before;
    DisplayProfile? requested;
    bool started, changed, persistentAttempt, terminal;
    public bool SafeToContinue { get; private set; } = true;
    public OperationResult Begin(DesktopSnapshot expected, DisplayProfile profile)
    {
        if (started) return OperationResult.Fail("此切换事务已经开始。");
        started = true;
        try
        {
            before = service.Capture();
            if (!SameLayout(expected.Displays, before.Displays)) throw new InvalidOperationException("显示器布局在准备期间发生变化，请刷新后重试。尚未切屏。");
            requested = profile with { Displays = profile.Displays.ToList() };
            LayoutPlanner.Check(requested, before.Displays);
            Require(service.Validate(requested));
            // Recovery information must exist before the first display write.
            journal(before);
            changed = true;
            SafeToContinue = false;
            Require(service.Apply(requested, false));
            return OperationResult.Ok("临时布局已应用，等待确认。");
        }
        catch (Exception e) { return FailAndRestore(e); }
    }
    public OperationResult Finish(bool keep)
    {
        if (terminal || !changed || before == null || requested == null) return OperationResult.Fail("此切换事务不能再次确认或恢复。");
        if (!keep)
        {
            terminal = true;
            try { var result = restore(before, false); SafeToContinue = result.Success; return result; } catch (Exception e) { return OperationResult.Fail("恢复异常：" + e.Message); }
        }
        try
        {
            if (service is IStableDisplayVerification verifier)
                Require(verifier.Verify(requested));
            else
            {
                var actual = service.Capture();
                Require(DisplayLimits.CheckConnected(actual.Displays));
                var expected = requested.Displays.Where(d => d.Enabled).ToList();
                if (actual.Displays.Count(d => d.Enabled) != expected.Count || expected.Any(t => !actual.Displays.Any(d => Matches(t, d)))) throw new InvalidOperationException("屏幕状态与预设不一致，未保存新布局。");
            }
            // Backup must succeed before Windows persistence is attempted.
            savePrevious(before);
            persistentAttempt = true;
            Require(service.Apply(requested, true));
            terminal = true;
            SafeToContinue = true;
            return OperationResult.Ok("场景已应用并保存。");
        }
        catch (Exception e) { return FailAndRestore(e); }
    }
    OperationResult FailAndRestore(Exception error)
    {
        terminal = true;
        var message = error.Message;
        if (changed && before != null)
        {
            try { var result = restore(before, persistentAttempt); SafeToContinue = result.Success; message = message.TrimEnd('。', '；', ' ') + (result.Success ? "，已恢复切换前的布局。" : "；恢复失败：" + result.Message); }
            catch (Exception e) { message += "；恢复异常：" + e.Message; }
        }
        return OperationResult.Fail(message);
    }
    static void Require(OperationResult result) { if (!result.Success) throw new InvalidOperationException(result.Message); }
    static bool Matches(DisplayTarget a, DisplayInfo b) => StringComparer.OrdinalIgnoreCase.Equals(a.Id, b.Id) && b.Connected && a.Enabled == b.Enabled && a.Primary == b.Primary && a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height && a.Rotation == b.Rotation && a.RefreshRate == b.RefreshRate;
    public static bool SameLayout(IReadOnlyList<DisplayInfo> a, IReadOnlyList<DisplayInfo> b)
    {
        var left = a.Where(d => d.Connected).ToList(); var right = b.Where(d => d.Connected).ToList();
        return left.Count == right.Count && left.All(d => right.Count(t => StringComparer.OrdinalIgnoreCase.Equals(d.Id, t.Id) && d.Enabled == t.Enabled && (!d.Enabled || Matches(new(d.Id, d.Enabled, d.Primary, d.X, d.Y, d.Width, d.Height, d.Rotation, d.RefreshRate), t))) == 1);
    }
}
