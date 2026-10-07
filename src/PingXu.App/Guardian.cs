using System.Diagnostics;
using System.IO;
using System.Text.Json;
using PingXu.Core;
using PingXu.Windows;
namespace PingXu.App;
public record TrialRequest(string Id, int OwnerPid, long OwnerStarted, DisplayProfile Profile, DesktopSnapshot Before,
    bool RequiresConfirmation = true, int TimeoutSeconds = 20);
public record TrialProgress(string Phase, string Message, int Seconds = 0, bool SafeToContinue = false);
public static class Guardian
{
    public static int Run(string path)
    {
        DisplayTransaction? transaction = null;
        void Progress(string phase, string message, int seconds = 0, bool safe = false) => ProfileStore.AtomicWrite(path + ".status", JsonSerializer.Serialize(new TrialProgress(phase, message, seconds, safe)));
        using var gate = new Mutex(false, "Local\\PingXu.DisplayTransaction." + Environment.UserName);
        bool owned = false;
        try
        {
            try { owned = gate.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
            if (!owned) { Progress("error", "已有显示切换正在进行。"); return 1; }
            var req = JsonSerializer.Deserialize<TrialRequest>(File.ReadAllText(path)) ?? throw new InvalidDataException("恢复事务为空。");
            new ConfirmationOptions(ConfirmationMode.Always, req.TimeoutSeconds).Validate();
            if (!OwnerAlive(req)) { Progress("error", "主界面已退出，未改变显示布局。", safe: true); return 1; }
            var service = new WindowsDisplayService(detail => Program.Log(new InvalidOperationException(detail)));
            transaction = new(service, service.Restore,
              before => ProfileStore.AtomicWrite(path + ".snapshot", JsonSerializer.Serialize(before, ProfileStore.Json)),
              before => ProfileStore.AtomicWrite(Path.Combine(Program.DataDirectory, "previous.json"), JsonSerializer.Serialize(before, ProfileStore.Json)));
            var clock = Stopwatch.StartNew();
            return Execute(req, transaction, () => OwnerAlive(req), () => File.Exists(path + ".cancel"),
                () => File.Exists(path + ".confirm"), p => ProfileStore.AtomicWrite(path + ".status", JsonSerializer.Serialize(p)),
                () => clock.Elapsed.TotalSeconds, () => Thread.Sleep(100));
        }
        catch (Exception ex) { Program.Log(ex); string msg = ex.Message; if (transaction != null) { try { var r = transaction.Finish(false); msg += "；" + r.Message; } catch (Exception re) { msg += "；恢复异常：" + re.Message; } } try { Progress("error", msg); } catch { } return 1; }
        finally { if (owned) gate.ReleaseMutex(); }
    }
    // Same state machine in production and isolated tests; the real runner supplies file IPC and a monotonic clock.
    public static int Execute(TrialRequest req, DisplayTransaction transaction, Func<bool> ownerAlive,
        Func<bool> cancelRequested, Func<bool> confirmed, Action<TrialProgress> progress, Func<double> seconds, Action wait)
    {
        new ConfirmationOptions(ConfirmationMode.Always, req.TimeoutSeconds).Validate();
        void Progress(string phase, string message, int seconds = 0, bool safe = false) => progress(new(phase, message, seconds, safe));
        if (!ownerAlive()) { Progress("error", "主界面已退出，未改变显示布局。", safe: true); return 1; }
        Progress("applying", "正在验证与切换显示输出…");
        var result = transaction.Begin(req.Before, req.Profile);
        if (!result.Success) { Progress("error", result.Message, safe: transaction.SafeToContinue); return 1; }
        var started = seconds(); var trial = new TrialState(req.TimeoutSeconds); int last = -1;
        // Direct commit still owns exactly the same journal, readback and failure-recovery transaction.
        // No pending trial or confirmation timer exists in the non-interactive path.
        bool keep;
        if (!req.RequiresConfirmation) keep = ownerAlive() && !cancelRequested();
        else
        {
            while (trial.Status == TrialStatus.Pending)
            {
                trial.Tick((seconds() - started));
                if (!ownerAlive() || cancelRequested()) trial.Cancel();
                if (confirmed()) trial.Confirm((seconds() - started));
                int remaining = Math.Max(0, req.TimeoutSeconds - (int)(seconds() - started));
                if (remaining != last) { last = remaining; Progress("trial", "请确认所有屏幕显示正常", remaining); }
                if (trial.Status == TrialStatus.Pending) wait();
            }
            keep = trial.Status == TrialStatus.Keep;
        }
        Progress(keep ? "committing" : "restoring", keep ? "正在保存确认的布局…" : "正在恢复切换前的布局…");
        result = transaction.Finish(keep);
        Progress(result.Success ? (keep ? "committed" : "reverted") : "error", result.Success ? (keep ? result.Message : "已恢复切换前的布局。") : result.Message, safe: transaction.SafeToContinue);
        return result.Success ? 0 : 1;
    }
    static bool OwnerAlive(TrialRequest req) { try { using var p = Process.GetProcessById(req.OwnerPid); return !p.HasExited && p.StartTime.ToUniversalTime().Ticks == req.OwnerStarted; } catch { return false; } }
}
