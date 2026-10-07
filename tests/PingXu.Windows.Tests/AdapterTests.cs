using PingXu.Core;
using PingXu.Windows;

internal static class AdapterTests
{
    internal static void Run(Action<string, Action> test)
    {
        RestoreTests.Run(test);
        RotationFallbackTests.Run(test);
        RawSourceContractTests.Run(test);
        test("Capture includes stable identities, disabled output, negative position and right primary", () =>
        {
            var s = new WindowsDisplayService(new FakeApi()).Capture();
            Check(s.Displays.Count == 3 && s.Displays[0].Id == "monitor-A" && s.Displays[0].X == -3840);
            Check(s.Displays.Single(d => d.Primary).Id == "monitor-B");
            Check(!s.Displays[2].Enabled && s.Displays[2].Connected);
            Check(s.Displays[0].DeviceName == @"\\.\DISPLAY1");
        });
        test("Rotation preserves unrotated source dimensions and exact 59.94 Hz target signal", () =>
        {
            var plan = CcdLogic.Build(Fixture(), Profile(new DisplayTarget("monitor-A", true, true, 0, 0, 2160, 3840, 90, 60)));
            Check(plan.Paths.Length == 1);
            var p = plan.Paths[0];
            var src = plan.Modes[p.Source.ModeIndex].Data.Source;
            var signal = plan.Modes[p.Target.ModeIndex].Data.Target;
            Check(src.Width == 3840 && src.Height == 2160 && p.Target.Rotation == 2);
            Check(signal.PixelRate == 594000000 && signal.ActiveSize.Width == 3840 && signal.ActiveSize.Height == 2160);
            Check(signal.VSync.Numerator == 60000 && signal.VSync.Denominator == 1001);
        });
        test("Multiple enabled targets allocate unique sources, valid compact indices and primary priority", () =>
        {
            var plan = CcdLogic.Build(Fixture(), Profile(
                new("monitor-A", true, false, -3840, 0, 3840, 2160, 0, 60),
                new("monitor-B", true, true, 0, 0, 1920, 1080, 0, 60),
                new("monitor-C", true, false, 1920, 0, 1920, 1080, 0, 60)));
            Check(plan.Paths.Length == 3 && plan.Paths.Select(p => p.Source.Id).Distinct().Count() == 3);
            Check(plan.Paths[0].Target.Id == 11);
            foreach (var p in plan.Paths)
            {
                Check(p.Source.ModeIndex < plan.Modes.Length && plan.Modes[p.Source.ModeIndex].Id == p.Source.Id);
                Check(p.Target.ModeIndex == uint.MaxValue || plan.Modes[p.Target.ModeIndex].Id == p.Target.Id);
            }
        });
        test("Changed resolution does not reuse old signal timing", () =>
        {
            var plan = CcdLogic.Build(Fixture(), Profile(new DisplayTarget("monitor-A", true, true, 0, 0, 1920, 1080, 0, 60)));
            Check(plan.Paths[0].Target.ModeIndex == uint.MaxValue);
            Check(plan.Modes[0].Data.Source.Width == 1920);
        });
        var invalid = new (string, DisplayProfile)[]
        {
            ("empty", Profile()),
            ("all-off", Profile(new DisplayTarget("monitor-A", false, false, 0, 0, 3840, 2160, 0, 60))),
            ("missing", Profile(new DisplayTarget("absent", true, true, 0, 0, 1920, 1080, 0, 60))),
            ("duplicate", Profile(new("monitor-A", true, true, 0, 0, 3840, 2160, 0, 60), new("MONITOR-A", false, false, 0, 0, 3840, 2160, 0, 60))),
            ("mode", Profile(new DisplayTarget("monitor-A", true, true, 0, 0, 1234, 777, 0, 60))),
            ("rotation", Profile(new DisplayTarget("monitor-A", true, true, 0, 0, 3840, 2160, 45, 60))),
            ("refresh", Profile(new DisplayTarget("monitor-A", true, true, 0, 0, 3840, 2160, 0, 0))),
            ("no primary", Profile(new DisplayTarget("monitor-A", true, false, 0, 0, 3840, 2160, 0, 60))),
            ("primary away from origin", Profile(new DisplayTarget("monitor-A", true, true, 12, 0, 3840, 2160, 0, 60))),
            ("overlap", Profile(new("monitor-A", true, true, 0, 0, 3840, 2160, 0, 60), new("monitor-B", true, false, 1, 1, 1920, 1080, 0, 60))),
            ("overflow", Profile(new("monitor-A", true, true, 0, 0, 3840, 2160, 0, 60), new("monitor-B", true, false, int.MaxValue, 0, 1920, 1080, 0, 60)))
        };
        foreach (var (name, profile) in invalid)
            test($"Reject {name} before native validation/apply", () =>
            {
                var api = new FakeApi();
                Check(!new WindowsDisplayService(api).Apply(profile, false).Success);
                Check(api.Flags.Count == 0);
            });
        test("Ambiguous physical identities are rejected", () =>
        {
            var api = new FakeApi();
            api.State.Outputs[1] = api.State.Outputs[1] with { Id = "MONITOR-A" };
            Check(!new WindowsDisplayService(api).Validate(One()).Success && api.Flags.Count == 0);
        });
        test("Clone topology is visibly unsupported", () =>
        {
            var state = Fixture(); state.Paths[1].Source = state.Paths[0].Source;
            Throws(() => CcdLogic.Describe(state));
        });
        test("Malformed active native mode index is rejected", () =>
        {
            var state = Fixture(); state.Paths[0].Source.ModeIndex = 999;
            Throws(() => CcdLogic.Describe(state));
        });
        test("Validate uses only SDC_VALIDATE and never mutates", () =>
        {
            var api = new FakeApi();
            Check(new WindowsDisplayService(api).Validate(One()).Success);
            Check(api.Flags.SequenceEqual(new uint[] { 0x60 }));
        });
        foreach (var persist in new[] { false, true })
            test($"Apply validates then applies with persist={persist} and reads back", () =>
            {
                var api = new FakeApi();
                Check(new WindowsDisplayService(api).Apply(One(), persist).Success);
                Check(api.Flags.SequenceEqual(new uint[] { 0x60, persist ? 0x2A0u : 0xA0u }));
                Check(api.ReadCount >= 2);
            });
        test("Native validation failure prevents application", () =>
        {
            var api = new FakeApi { ValidateError = 1610 };
            Check(!new WindowsDisplayService(api).Apply(One(), false).Success);
            Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0x460 }));
        });
        test("Readback difference cannot report success", () =>
        {
            var api = new FakeApi { IgnoreApply = true };
            var result = new WindowsDisplayService(api).Apply(One(), false);
            Check(!result.Success && result.Message.Contains("未能确认"));
        });
        test("Console access denied is Chinese and never suggests elevation", () =>
        {
            var api = new FakeApi { ValidateError = 5 };
            var result = new WindowsDisplayService(api).Validate(One());
            Check(!result.Success && result.Message.Contains("Windows 错误 5") && !result.Message.Contains("会话"));
            Check(!result.Message.Contains("管理员"));
        });
        test("Native snapshot JSON preserves every raw path and mode byte", () =>
        {
            var state = Fixture(); var json = SnapshotCodec.Encode(state);
            var restored = SnapshotCodec.Decode(json);
            Check(restored.Paths.Length == 4 && restored.Modes.Length == 4);
            Check(restored.Modes[1].Data.Target.TotalSize.Width == 4400);
            Check(restored.Modes[1].Data.Target.PixelRate == 594000000);
            Check(restored.Paths[0].Source.StatusFlags == 1 && restored.Outputs[0].Id == "monitor-A");
            Check(json.StartsWith('{'));
        });
        foreach (var json in new[] { "", "{}", "null", "{broken", "{\"Version\":999}" })
            test($"Corrupt snapshot is rejected: {json}", () =>
            {
                var api = new FakeApi();
                Check(!new WindowsDisplayService(api).Restore(new([], json)).Success && api.Flags.Count == 0);
            });
        test("Restore rebinds changed adapter LUID and target/source IDs before any native call", () =>
        {
            var original = Fixture(); var current = Readdress(Fixture());
            var plan = CcdLogic.Rebind(SnapshotCodec.Decode(SnapshotCodec.Encode(original)), current);
            Check(plan.Paths.Length == 2);
            foreach (var p in plan.Paths)
            {
                Check(p.Source.AdapterId.Low == 99 && p.Target.AdapterId.Low == 99);
                Check(p.Target.Id >= 110 && p.Source.Id >= 100);
                Check(plan.Modes[p.Source.ModeIndex].AdapterId.Low == 99);
                Check(plan.Modes[p.Target.ModeIndex].AdapterId.Low == 99);
            }
            Check(plan.Modes[plan.Paths[0].Target.ModeIndex].Data.Target.PixelRate == 594000000);
        });
        test("Restore validates, applies temporarily and checks native readback", () =>
        {
            var api = new FakeApi { State = Readdress(Fixture()) };
            var saved = new DesktopSnapshot([], SnapshotCodec.Encode(Fixture()));
            Check(new WindowsDisplayService(api).Restore(saved).Success);
            Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0xA0 }));
        });
        test("Unavailable restore topology fails visibly without mutation", () =>
        {
            var api = new FakeApi(); api.State.Paths[0].Target.Available = 0;
            var result = new WindowsDisplayService(api).Restore(new([], SnapshotCodec.Encode(Fixture())));
            Check(!result.Success && api.Flags.Count == 0);
        });
        test("Capture portrait reports desktop dimensions and unrotated supported mode", () =>
        {
            var state = Fixture(); state.Paths[0].Target.Rotation = 2;
            state.Modes[0].Data.Source.Width = 3840; state.Modes[0].Data.Source.Height = 2160;
            var display = CcdLogic.Describe(state)[0];
            Check(display.Width == 2160 && display.Height == 3840 && display.Rotation == 90);
            Check(display.Modes.Contains(new(3840, 2160, 60)) && !display.Modes.Contains(new(2160, 3840, 60)));
        });
        test("Absent path refresh uses signal refresh and still preserves rotation timing", () =>
        {
            var state = Fixture(); state.Paths[0].Target.RefreshRate = default;
            var plan = CcdLogic.Build(state, One());
            Check(plan.Paths[0].Target.ModeIndex != uint.MaxValue);
            Check(plan.Modes[plan.Paths[0].Target.ModeIndex].Data.Target.VSync.Denominator == 1001);
        });
        test("Rerouted active output retains its original scaling", () =>
        {
            var state = Fixture(); state.Paths[0].Target.Scaling = 2;
            var alternate = state.Paths[0]; alternate.Flags = 0; alternate.Source.Id = 2;
            alternate.Source.ModeIndex = alternate.Target.ModeIndex = uint.MaxValue; alternate.Target.Scaling = 0;
            state = state with { Paths = new[] { state.Paths[0], state.Paths[1], state.Paths[2], alternate } };
            var plan = CcdLogic.Build(state, Profile(
                new("monitor-A", true, true, 0, 0, 3840, 2160, 0, 60),
                new("monitor-C", true, false, 3840, 0, 1920, 1080, 0, 60)));
            var path = plan.Paths.Single(p => p.Target.Id == 10);
            Check(path.Source.Id == 2 && path.Target.Scaling == 2);
        });
        test("Insufficient source capacity cannot become an accidental clone", () =>
        {
            var state = Fixture(); state = state with { Paths = state.Paths.Take(3).ToArray() };
            Throws(() => CcdLogic.Build(state, Profile(
                new("monitor-A", true, true, 0, 0, 3840, 2160, 0, 60),
                new("monitor-C", true, false, 3840, 0, 1920, 1080, 0, 60))));
        });
        test("Native apply failure is visible", () =>
        {
            var api = new FakeApi { ApplyError = 31 };
            Check(!new WindowsDisplayService(api).Apply(One(), false).Success);
            Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0xA0 }));
        });
        test("Restore rejects changed timing even when integer refresh matches", () =>
        {
            var api = new FakeApi { AlterSignalAfterApply = true };
            var result = new WindowsDisplayService(api).Restore(new([], SnapshotCodec.Encode(Fixture())));
            Check(!result.Success && result.Message.Contains("回读"));
        });
        test("Snapshot rejects invalid raw mode indices before native calls", () =>
        {
            var bad = Fixture(); bad.Paths[0].Target.ModeIndex = 5000;
            var api = new FakeApi();
            Check(!new WindowsDisplayService(api).Restore(new([], SnapshotCodec.Encode(bad))).Success && api.Flags.Count == 0);
        });
        test("Null profile and snapshot return visible failure without native calls", () =>
        {
            var api = new FakeApi(); var service = new WindowsDisplayService(api);
            Check(!service.Apply(null!, false).Success && !service.Validate(null!).Success && !service.Restore(null!).Success);
            Check(api.Flags.Count == 0);
        });
        test("Mutation-bound input list is copied before OS boundary calls", () =>
        {
            var profile = One(); var api = new FakeApi { AfterValidation = () => profile.Displays.Clear() };
            Check(new WindowsDisplayService(api).Apply(profile, false).Success);
        });
    }

    internal static DisplayProfile Profile(params DisplayTarget[] targets) => new("test", "测试", targets.ToList());
    internal static DisplayProfile One() => Profile(new DisplayTarget("monitor-A", true, true, 0, 0, 2160, 3840, 90, 60));
    internal static void Check(bool condition) { if (!condition) throw new Exception("Behavior assertion failed"); }
    internal static void Throws(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Invalid input was accepted");
    }
    internal static CcdState Fixture()
    {
        var luid = new Luid { Low = 7 };
        var modes = new List<ModeInfo>(); var paths = new List<PathInfo>(); var outputs = new List<Output>();
        for (uint i = 0; i < 3; i++)
        {
            var width = i == 0 ? 3840u : 1920u; var height = i == 0 ? 2160u : 1080u;
            var signal = new VideoSignal { PixelRate = i == 0 ? 594000000ul : 148500000ul,
                HSync = new() { Numerator = 135000, Denominator = 1 },
                VSync = new() { Numerator = 60000, Denominator = 1001 },
                ActiveSize = new() { Width = width, Height = height },
                TotalSize = new() { Width = i == 0 ? 4400u : 2200u, Height = i == 0 ? 2250u : 1125u }, ScanLineOrdering = 1 };
            if (i < 2)
            {
                modes.Add(new() { Type = 1, Id = i, AdapterId = luid, Data = new() { Source = new() {
                    Width = width, Height = height, PixelFormat = 4, Position = new() { X = i == 0 ? -3840 : 0 } } } });
                modes.Add(new() { Type = 2, Id = i + 10, AdapterId = luid, Data = new() { Target = signal } });
            }
            paths.Add(new() { Flags = i < 2 ? 1u : 0u,
                Source = new() { AdapterId = luid, Id = i, ModeIndex = i < 2 ? i * 2 : uint.MaxValue, StatusFlags = i < 2 ? 1u : 0u },
                Target = new() { AdapterId = luid, Id = i + 10, ModeIndex = i < 2 ? i * 2 + 1 : uint.MaxValue,
                    Available = 1, Rotation = 1, Scaling = 1, RefreshRate = signal.VSync, ScanLineOrdering = 1, OutputTechnology = 5 } });
            outputs.Add(new(new(luid, i + 10), $"monitor-{(char)('A' + i)}", "显示器", $@"\\.\DISPLAY{i + 1}",
                i == 0 ? [new(3840, 2160, 60), new(1920, 1080, 60)] : [new(1920, 1080, 60)], signal));
        }
        var conflicting = paths[2]; conflicting.Source.Id = 0; paths.Insert(2, conflicting);
        return new(paths.ToArray(), modes.ToArray(), outputs.ToArray());
    }
    internal static CcdState Readdress(CcdState state)
    {
        for (var i = 0; i < state.Paths.Length; i++)
        {
            state.Paths[i].Source.AdapterId.Low = 99; state.Paths[i].Source.Id += 100;
            state.Paths[i].Target.AdapterId.Low = 99; state.Paths[i].Target.Id += 100;
        }
        for (var i = 0; i < state.Modes.Length; i++) { state.Modes[i].AdapterId.Low = 99; state.Modes[i].Id += 100; }
        for (var i = 0; i < state.Outputs.Length; i++)
            state.Outputs[i] = state.Outputs[i] with { Target = new(new() { Low = 99 }, state.Outputs[i].Target.Id + 100) };
        return state;
    }
}

// Fake only the OS boundary: all planning, marshalled data, JSON and readback comparison stay real.
internal sealed class FakeApi : ICcdApi
{
    internal CcdState State = AdapterTests.Fixture();
    internal List<uint> Flags = [];
    internal int ReadCount, ValidateError, ApplyError, FailOnRead;
    internal bool IgnoreApply, AlterSignalAfterApply;
    internal Action? AfterValidation;
    public CcdState Read()
    {
        ReadCount++;
        if (ReadCount == FailOnRead) throw new InvalidOperationException("模拟回读失败：显示设备已断开。");
        return State;
    }
    public int Set(CcdPlan plan, uint flags)
    {
        Flags.Add(flags);
        if ((flags & 0x40) != 0) { AfterValidation?.Invoke(); return ValidateError; }
        if (ApplyError != 0) return ApplyError;
        if (!IgnoreApply)
        {
            // Preserve inactive possible routes, as QDC_ALL_PATHS does.
            var inactive = State.Paths.Where(p => !plan.Paths.Any(a => a.Target.Id == p.Target.Id && a.Target.AdapterId.Equals(p.Target.AdapterId)))
                .Select(p => { p.Flags = 0; p.Source.ModeIndex = p.Target.ModeIndex = uint.MaxValue; return p; });
            State = State with { Paths = plan.Paths.Concat(inactive).ToArray(), Modes = plan.Modes };
            if (AlterSignalAfterApply)
                for (var i = 0; i < State.Modes.Length; i++)
                    if (State.Modes[i].Type == 2) State.Modes[i].Data.Target.PixelRate++;
        }
        return 0;
    }
}
