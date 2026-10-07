using System.IO;
using System.Text.Json;
using PingXu.Core;

namespace PingXu.App.Tests;

internal static class ConfirmationIntegrationTests
{
    public static void Register(Action<string, Action> test)
    {
        void Check(bool condition) { if (!condition) throw new Exception("Confirmation integration assertion failed"); }
        test("驱动指纹只读取四位实例，不把受保护的类Properties分支当驱动", () =>
        {
            Check(DisplayEnvironment.IsDriverInstance("0000") && DisplayEnvironment.IsDriverInstance("0001"));
            Check(!DisplayEnvironment.IsDriverInstance("Properties") && !DisplayEnvironment.IsDriverInstance("Configuration") && !DisplayEnvironment.IsDriverInstance("１２３４"));
        });
        test("旧偏好迁移默认智能确认；别名与快捷键保存不抹掉策略和信任", () =>
        {
            var dir = Directory.CreateTempSubdirectory("PingXu-preferences-test-");
            try
            {
                ProfileStore.AtomicWrite(Path.Combine(dir.FullName, "preferences.json"), "{\"Hotkeys\":false,\"Aliases\":{\"screen\":\"阅读屏\"}}");
                Check(Preferences.Load(dir.FullName).Confirmation == new ConfirmationOptions());
                var key = "v1:" + new string('A', 64);
                (Preferences.Load(dir.FullName) with { Confirmation = new(ConfirmationMode.Never, 60) }).Save(dir.FullName);
                Preferences.Remember(key, dir.FullName);
                Preferences.SaveInterface(true, new() { ["screen"] = "4K" }, dir.FullName);
                var saved = Preferences.Load(dir.FullName);
                Check(saved.Hotkeys && saved.Aliases["screen"] == "4K" && saved.Confirmation == new ConfirmationOptions(ConfirmationMode.Never, 60));
                Check(saved.TrustedProfiles.SequenceEqual([key]));
                Preferences.Remember(key, dir.FullName);
                Check(Preferences.Load(dir.FullName).TrustedProfiles.Count == 1);
                (saved with { TrustedProfiles = [] }).Save(dir.FullName);
                Check(Preferences.Load(dir.FullName).TrustedProfiles.Count == 0);
            }
            finally { dir.Delete(true); }
        });
        test("未知确认模式不能误读为不再确认，坏文件不覆盖", () =>
        {
            var dir = Directory.CreateTempSubdirectory("PingXu-invalid-policy-test-");
            try
            {
                var path = Path.Combine(dir.FullName, "preferences.json");
                const string invalid = "{\"Hotkeys\":false,\"Aliases\":{},\"Confirmation\":{\"Mode\":99,\"TimeoutSeconds\":20}}";
                ProfileStore.AtomicWrite(path, invalid);
                try { Preferences.SaveInterface(true, [], dir.FullName); throw new Exception("invalid policy accepted"); }
                catch (ArgumentOutOfRangeException) { }
                Check(File.ReadAllText(path) == invalid);
            }
            finally { dir.Delete(true); }
        });
        test("旧看护请求缺少新字段时默认20秒确认，不默认直切", () =>
        {
            var json = JsonSerializer.Serialize(new { Id = "test", OwnerPid = 1, OwnerStarted = 1L, Profile = Next, Before = Original });
            var request = JsonSerializer.Deserialize<TrialRequest>(json)!;
            Check(request.RequiresConfirmation && request.TimeoutSeconds == 20);
        });
        test("实际看护执行缝：直切无trial/无等待，仍先临时再持久化", () =>
        {
            var r = Run(false, 20);
            Check(r.Exit == 0 && r.Progress.Last().Phase == "committed" && r.Elapsed == 0);
            Check(r.Progress.All(p => p.Phase != "trial") && r.Service.Applies.SequenceEqual([false, true]) && r.Service.Restored == null);
        });
        foreach (var duration in new[] { 20, 30, 60 })
            test($"实际看护执行缝：{duration}秒超时恢复，首个倒计时正确", () =>
            {
                var r = Run(true, duration);
                Check(r.Exit == 0 && r.Progress.Last().Phase == "reverted" && r.Elapsed == duration);
                Check(r.Progress.First(p => p.Phase == "trial").Seconds == duration && r.Service.Restored == Original);
            });
        test("实际看护执行缝：人工确认后没有残留计时恢复", () =>
        {
            var r = Run(true, 30, confirmAt: 5);
            Check(r.Elapsed == 5 && r.Progress.Last().Phase == "committed" && r.Service.Restored == null);
        });
        test("实际看护执行缝：到期与确认同时发生时到期优先", () =>
        {
            var r = Run(true, 30, confirmAt: 30);
            Check(r.Progress.Last().Phase == "reverted" && !r.Service.Applies.Contains(true));
        });
        test("实际看护执行缝：直切取消优先于提交", () =>
        {
            var r = Run(false, 20, cancelAt: 0);
            Check(r.Progress.Last().Phase == "reverted" && r.Service.Restored == Original);
        });
        foreach (var persistent in new[] { false, true })
            test($"实际看护执行缝：直切失败仍恢复，persistent={persistent}", () =>
            {
                var r = Run(false, 20, fail: persistent ? "persist" : "temporary");
                Check(r.Exit == 1 && r.Progress.Last().Phase == "error" && r.Progress.Last().SafeToContinue);
                Check(r.Service.Restored == Original && r.Service.PersistRestore == persistent);
            });
        test("实际看护执行缝：直切失败且恢复失败不会解除锁", () =>
        {
            var r = Run(false, 20, fail: "restore");
            Check(r.Exit == 1 && !r.Progress.Last().SafeToContinue);
        });
    }
    static readonly DesktopSnapshot Original = new([new("screen", "FAKE", "4K", true, true, true, 0, 0, 3840, 2160, 0, 60, [])], "original");
    static readonly DisplayProfile Next = new("p", "竖屏", [new("screen", true, true, 0, 0, 2160, 3840, 90, 60)]);
    record Result(int Exit, List<TrialProgress> Progress, RecordingService Service, double Elapsed);
    static Result Run(bool confirm, int duration, double confirmAt = double.PositiveInfinity, double cancelAt = double.PositiveInfinity, string? fail = null)
    {
        var service = new RecordingService { Failure = fail };
        var transaction = new DisplayTransaction(service, service.RestoreExact, _ => { }, _ => { });
        var progress = new List<TrialProgress>(); double elapsed = 0;
        int exit = Guardian.Execute(new("test", 1, 1, Next, Original, confirm, duration), transaction,
            () => true, () => elapsed >= cancelAt, () => elapsed >= confirmAt, progress.Add, () => elapsed, () => elapsed++);
        return new(exit, progress, service, elapsed);
    }
    sealed class RecordingService : IDisplayService
    {
        public DesktopSnapshot Current = Original;
        public DesktopSnapshot? Restored;
        public List<bool> Applies = [];
        public bool PersistRestore;
        public string? Failure;
        public DesktopSnapshot Capture() => Current;
        public OperationResult Validate(DisplayProfile p) => OperationResult.Ok();
        public OperationResult Apply(DisplayProfile p, bool persist)
        {
            Applies.Add(persist);
            Current = new(p.Displays.Select(d => new DisplayInfo(d.Id, "FAKE", "4K", true, d.Enabled, d.Primary, d.X, d.Y, d.Width, d.Height, d.Rotation, d.RefreshRate, [])).ToList(), "changed");
            return (persist && Failure == "persist") || (!persist && Failure is "temporary" or "restore") ? OperationResult.Fail("synthetic driver failure") : OperationResult.Ok();
        }
        public OperationResult Restore(DesktopSnapshot s) => RestoreExact(s, false);
        public OperationResult RestoreExact(DesktopSnapshot s, bool persist)
        {
            Restored = s; PersistRestore = persist; Current = s;
            return Failure == "restore" ? OperationResult.Fail("synthetic restore failure") : OperationResult.Ok();
        }
    }
}
