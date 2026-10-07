using PingXu.Core;
using PingXu.Windows;
using static AdapterTests;

internal static class RestoreTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("Interface Restore remains temporary and checks readback", () =>
        {
            var api = new FakeApi();
            IDisplayService service = new WindowsDisplayService(api);
            var result = service.Restore(Saved());
            Check(result.Success && result.Message.Contains("临时"));
            Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0xA0 }) && api.ReadCount == 2);
        });
        foreach (var persist in new[] { false, true })
            test($"Restore persist={persist} validates first and submits exact rebound native data", () =>
            {
                var original = Fixture();
                original.Paths[0].Target.Scaling = 2;
                original.Paths[0].Target.Rotation = 2;
                original.Modes[0].Data.Source.Width = 3840;
                original.Modes[0].Data.Source.Height = 2160;
                var api = new FakeApi { State = Readdress(Fixture()) };
                // Deliberately inconsistent public display list: native snapshot must remain authoritative.
                var snapshot = new DesktopSnapshot([], SnapshotCodec.Encode(original));
                var result = Restore(new WindowsDisplayService(api), snapshot, persist);
                Check(result.Success && result.Message.Contains(persist ? "保存" : "临时"));
                Check(api.Flags.SequenceEqual(new uint[] { 0x60, persist ? 0x2A0u : 0xA0u }) && api.ReadCount == 2);
                var path = api.State.Paths.Single(p => CcdLogic.Active(p) && p.Target.Id == 110);
                Check(path.Source.AdapterId.Low == 99 && path.Target.AdapterId.Low == 99 && path.Source.Id == 100);
                Check(path.Target.Scaling == 2 && path.Target.Rotation == 2);
                Check(SnapshotCodec.Bytes(new[] { original.Modes[0].Data.Source }).SequenceEqual(
                    SnapshotCodec.Bytes(new[] { api.State.Modes[path.Source.ModeIndex].Data.Source })));
                Check(SnapshotCodec.Bytes(new[] { original.Modes[1].Data.Target }).SequenceEqual(
                    SnapshotCodec.Bytes(new[] { api.State.Modes[path.Target.ModeIndex].Data.Target })));
            });
        test("Persistent restore validation error stops before save and propagates Windows code", () =>
        {
            var api = new FakeApi { ValidateError = 1610 };
            var result = Restore(new WindowsDisplayService(api), Saved(), true);
            Check(!result.Success && result.Message.Contains("1610") && result.Message.Contains("验证"));
            Check(api.Flags.SequenceEqual(new uint[] { 0x60 }) && api.ReadCount == 1);
        });
        foreach (var error in new[] { 5, 31 })
            test($"Persistent restore apply error {error} propagates without temporary fallback", () =>
            {
                var api = new FakeApi { ApplyError = error };
                var result = Restore(new WindowsDisplayService(api), Saved(), true);
                Check(!result.Success && result.Message.Contains(error.ToString()) && result.Message.Contains("恢复"));
                Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0x2A0 }) && api.ReadCount == 1);
            });
        test("Persistent restore rejects changed native timing despite matching integer Hz", () =>
        {
            var api = new FakeApi { AlterSignalAfterApply = true };
            var result = Restore(new WindowsDisplayService(api), Saved(), true);
            Check(!result.Success && result.Message.Contains("回读") && result.Message.Contains("原生信号"));
            Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0x2A0 }));
        });
        test("Persistent restore rejects different desktop readback", () =>
        {
            var api = new FakeApi { IgnoreApply = true };
            api.State.Modes[0].Data.Source.Position.X = -4000;
            var result = Restore(new WindowsDisplayService(api), Saved(), true);
            Check(!result.Success && result.Message.Contains("回读")
                && result.Message.Contains("(-3840,0)") && result.Message.Contains("(-4000,0)"));
            Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0x2A0 }));
        });
        test("Persistent restore exposes post-save read failure", () =>
        {
            var api = new FakeApi { FailOnRead = 2 };
            var result = Restore(new WindowsDisplayService(api), Saved(), true);
            Check(!result.Success && result.Message.Contains("已尝试恢复") && result.Message.Contains("显示设备已断开"));
            Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0x2A0 }));
        });
        test("Persistent restore rejects corrupt native snapshot before any OS call", () =>
        {
            var api = new FakeApi();
            Check(!Restore(new WindowsDisplayService(api), new([], "{broken"), true).Success);
            Check(api.Flags.Count == 0 && api.ReadCount == 0);
        });
        test("Persistent restore cannot save unavailable topology", () =>
        {
            var api = new FakeApi(); api.State.Paths[0].Target.Available = 0;
            Check(!Restore(new WindowsDisplayService(api), Saved(), true).Success);
            Check(api.Flags.Count == 0);
        });
    }
    private static DesktopSnapshot Saved() => new([], SnapshotCodec.Encode(Fixture()));
    private static OperationResult Restore(WindowsDisplayService service, DesktopSnapshot snapshot, bool persist)
        => service.Restore(snapshot, persist);
}
