using PingXu.Core;
using PingXu.Windows;
using static AdapterTests;

internal static class ReenableTests
{
    // Historical desktop snapshots are read only; no native API is constructed by this replay.
    internal static void ReplaySnapshots(string directory, Action<string, Action> test)
    {
        var files = Directory.GetFiles(directory, "*.snapshot").Order().ToArray();
        if (files.Length == 0) throw new InvalidOperationException("No historical snapshots found.");
        for (var n = 0; n < files.Length; n++)
        {
            var text = File.ReadAllText(files[n]);
            var saved = System.Text.Json.JsonSerializer.Deserialize<DesktopSnapshot>(text)
                ?? throw new InvalidOperationException("Invalid historical snapshot.");
            var original = SnapshotCodec.Decode(saved.NativeData);
            var active = CcdLogic.Describe(original).Where(d => d.Enabled).ToArray();
            if (active.Length < 2) continue;
            foreach (var selected in active)
                test($"Historical snapshot {n + 1}, target {Array.IndexOf(active, selected) + 1}: disable then reenable", () =>
                {
                    var api = new DisabledApi { State = SnapshotCodec.Decode(saved.NativeData) };
                    var service = new WindowsDisplayService(api);
                    var retained = active.Where(d => d.Id != selected.Id).ToArray();
                    var primary = retained.FirstOrDefault(d => d.Primary) ?? retained[0];
                    Success(service.Apply(Profile(retained.Select(d => new DisplayTarget(d.Id, true, d.Id == primary.Id,
                        d.X - primary.X, d.Y - primary.Y, d.Width, d.Height, d.Rotation, d.RefreshRate)).ToArray()), false));
                    var afterDisable = service.Capture();
                    var disabled = afterDisable.Displays.Single(d => d.Id == selected.Id);
                    Check(disabled.Connected && !disabled.Enabled);
                    var mode = DisplayEnableMode.Select(disabled);
                    var targets = afterDisable.Displays.Where(d => d.Enabled).Select(d => new DisplayTarget(d.Id, true,
                        d.Primary, d.X, d.Y, d.Width, d.Height, d.Rotation, d.RefreshRate)).ToList();
                    var right = targets.Max(d => checked(d.X + d.Width));
                    targets.Add(new(disabled.Id, true, false, right, 0, mode.Width, mode.Height, 0, mode.RefreshRate));
                    var request = Profile(targets.ToArray());
                    api.RequireSolver = true;
                    Success(service.Validate(request));
                    Success(service.Apply(request, false));
                    Check(service.Capture().Displays.Single(d => d.Id == selected.Id).Enabled);
                    Check(File.ReadAllText(files[n]) == text);
                });
        }
    }

    internal static void Run(Action<string, Action> test)
    {
        foreach (var error in new[] { 0, 5, 31 })
            test($"Reenable diagnostics require a working baseline, error={error}", () =>
            {
                var api = new FakeApi { ValidateError = error }; var output = new StringWriter();
                Check(ReenableDiagnostics.BaselineAllowsProbes(api, CcdLogic.Build(Fixture(), One()), output) == (error == 0));
                Check(api.Flags.SequenceEqual(new uint[] { 0x60 }));
                Check(output.ToString().Contains("INCONCLUSIVE") == (error != 0));
            });
        test("Error 5 does not claim the caller is in a remote session", () =>
        {
            var api = new FakeApi { ValidateError = 5 };
            var result = new WindowsDisplayService(api).Validate(One());
            Check(!result.Success && result.Message.Contains("Windows 错误 5"));
            Check(!result.Message.Contains("远程") && !result.Message.Contains("会话") && !result.Message.Contains("拓扑"));
            Check(result.Message.Contains("具体原因尚未确定") && api.Flags.SequenceEqual(new uint[] { 0x60 }));
        });
        test("New primary does not steal a retained active source when its own route is free", () =>
        {
            var state = Fixture();
            var alternate = state.Paths[0]; alternate.Flags = 0; alternate.Source.Id = 3;
            alternate.Source.ModeIndex = alternate.Target.ModeIndex = uint.MaxValue;
            state = state with { Paths = state.Paths.Append(alternate).ToArray() };
            var plan = CcdLogic.Build(state, Profile(
                new("monitor-C", true, true, 0, 0, 1920, 1080, 0, 60),
                new("monitor-A", true, false, 1920, 0, 3840, 2160, 0, 60)));
            Check(plan.Paths[0].Target.Id == 12);
            var actual = plan.Paths.Single(p => p.Target.Id == 10).Source.Id;
            if (actual != 0) throw new Exception($"Retained monitor-A source moved from 0 to {actual}.");
            Check(plan.Paths.Single(p => p.Target.Id == 12).Source.Id == 2);
        });
        test("Reenabled target keeps its exact preferred signal including fractional refresh", () =>
        {
            var state = Fixture();
            var plan = CcdLogic.Build(state, Profile(
                new("monitor-A", true, true, 0, 0, 3840, 2160, 0, 60),
                new("monitor-C", true, false, 3840, 0, 1920, 1080, 0, 60)));
            var path = plan.Paths.Single(p => p.Target.Id == 12);
            if (path.Target.ModeIndex == uint.MaxValue) throw new Exception("Target's known preferred timing was discarded.");
            Check(path.Target.RefreshRate.Numerator == 60000 && path.Target.RefreshRate.Denominator == 1001);
            Check(SnapshotCodec.Bytes(new[] { plan.Modes[path.Target.ModeIndex].Data.Target })
                .SequenceEqual(SnapshotCodec.Bytes(new[] { state.Outputs[2].PreferredSignal!.Value })));
        });
        test("Disable then reenable connected target when GDI mode enumeration becomes empty", () =>
        {
            var api = new DisabledApi(); var service = new WindowsDisplayService(api);
            var before = service.Capture();
            Success(service.Apply(OnlyB(), false));
            var disabled = service.Capture().Displays.Single(d => d.Id == "monitor-A");
            Check(disabled.Connected && !disabled.Enabled);
            var old = before.Displays.Single(d => d.Id == disabled.Id);
            Success(service.Validate(Profile(
                new("monitor-B", true, true, 0, 0, 1920, 1080, 0, 60),
                new(old.Id, true, false, 1920, 0, old.Width, old.Height, old.Rotation, old.RefreshRate))));
            Success(service.Apply(Extended(), false));
            Check(service.Capture().Displays.Single(d => d.Id == "monitor-A").Enabled);
        });
        test("Disable then reenable requests driver timing solver only after strict validation fails", () =>
        {
            var api = new DisabledApi { KeepModes = true, RequireSolver = true };
            var service = new WindowsDisplayService(api);
            Success(service.Apply(OnlyB(), false));
            Success(service.Validate(Extended()));
            Check(api.Flags.TakeLast(2).SequenceEqual(new uint[] { 0x60, 0x460 }));
            Success(service.Apply(Extended(), false));
            Check(service.Capture().Displays.Single(d => d.Id == "monitor-A").Enabled);
        });
        foreach (var error in new[] { 31, 1610 })
            foreach (var persist in new[] { false, true })
                test($"Reenable strict error {error} persist={persist} preserves request and validates before applying", () =>
                {
                    var api = Disabled(); api.RequireSolver = true; api.StrictError = error;
                    var service = new WindowsDisplayService(api); var before = service.Capture();
                    Success(service.Validate(Extended()));
                    Check(SnapshotCodec.Bytes(api.State.Paths).SequenceEqual(SnapshotCodec.Bytes(SnapshotCodec.Decode(before.NativeData).Paths)));
                    Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0x460 }));
                    api.Flags.Clear(); api.Plans.Clear();
                    Success(service.Apply(Extended(), persist));
                    Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0x460, persist ? 0x6A0u : 0x4A0u }));
                    foreach (var plan in api.Plans.Skip(1))
                    {
                        Check(SnapshotCodec.Bytes(plan.Paths).SequenceEqual(SnapshotCodec.Bytes(api.Plans[0].Paths)));
                        Check(SnapshotCodec.Bytes(plan.Modes).SequenceEqual(SnapshotCodec.Bytes(api.Plans[0].Modes)));
                    }
                });
        foreach (var error in new[] { 5, 87, 50, 1167 })
            test($"Reenable error {error} never retries or applies", () =>
            {
                var api = Disabled(); api.RequireSolver = true; api.StrictError = error;
                var result = new WindowsDisplayService(api).Apply(Extended(), true);
                Check(!result.Success && result.Message.Contains($"Windows 错误 {error}"));
                Check(api.Flags.SequenceEqual(new uint[] { 0x60 }));
            });
        foreach (var error in new[] { 5, 31, 1610 })
            test($"Reenable solver failure {error} never applies", () =>
            {
                var api = Disabled(); api.RequireSolver = true; api.SolverError = error;
                var result = new WindowsDisplayService(api).Apply(Extended(), false);
                Check(!result.Success && result.Message.Contains($"Windows 错误 {error}"));
                Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0x460 }));
            });
        foreach (var field in new[] { "width", "rotation", "position", "rate", "missing-target" })
            test($"Reenable solver readback drift {field} cannot report success", () =>
            {
                var api = Disabled(); api.RequireSolver = true;
                api.AfterReenableApply = state =>
                {
                    var index = Array.FindIndex(state.Paths, p => p.Target.Id == 10);
                    ref var path = ref state.Paths[index];
                    switch (field)
                    {
                        case "width": state.Modes[path.Source.ModeIndex].Data.Source.Width--; break;
                        case "rotation": path.Target.Rotation = 2; break;
                        case "position": state.Modes[path.Source.ModeIndex].Data.Source.Position.X++; break;
                        case "rate": path.Target.RefreshRate = new() { Numerator = 59, Denominator = 1 }; break;
                        case "missing-target": path.Flags = 0; break;
                    }
                };
                var result = new WindowsDisplayService(api).Apply(Extended(), false);
                Check(!result.Success && result.Message.Contains("未能确认"));
            });
        foreach (var drift in new[] { false, true })
            test($"Guardian restore reenable keeps exact native readback contract drift={drift}", () =>
            {
                var api = new DisabledApi(); var service = new WindowsDisplayService(api); var saved = service.Capture();
                Success(service.Apply(OnlyB(), false)); api.Flags.Clear(); api.RequireSolver = true;
                if (drift) api.AfterReenableApply = state =>
                {
                    var path = state.Paths.Single(p => p.Target.Id == 10);
                    state.Modes[path.Target.ModeIndex].Data.Target.PixelRate++;
                };
                var result = service.Restore(saved, true);
                Check(result.Success != drift);
                if (drift) Check(result.Message.Contains("原生信号"));
                Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0x460, 0x6A0 }));
            });
        test("Zero inactive desktop dimensions choose advertised mode and complete reenable", () =>
        {
            var api = Disabled();
            api.State.Outputs[0] = api.State.Outputs[0] with { PreferredSignal = null, Modes = [new(3840, 2160, 60)] };
            var service = new WindowsDisplayService(api);
            var disabled = service.Capture().Displays.Single(d => d.Id == "monitor-A");
            Check(disabled.Width == 0 && disabled.Height == 0 && disabled.RefreshRate == 0);
            var mode = DisplayEnableMode.Select(disabled);
            Check(mode == new DisplayMode(3840, 2160, 60));
            // The independently simulated driver can supply target timing even if GET_PREFERRED_MODE failed.
            Success(service.Apply(Profile(
                new("monitor-B", true, true, 0, 0, 1920, 1080, 0, 60),
                new(disabled.Id, true, false, 1920, 0, mode.Width, mode.Height, 0, mode.RefreshRate)), false));
        });
        test("Absent preferred timing and empty mode list do not invent a desktop mode", () =>
        {
            var api = Disabled(); api.State.Outputs[0] = api.State.Outputs[0] with { PreferredSignal = null };
            var service = new WindowsDisplayService(api);
            var d = service.Capture().Displays.Single(d => d.Id == "monitor-A");
            Check(!DisplayEnableMode.TrySelect(d, out _));
            Throws(() => DisplayEnableMode.Select(d));
            Check(!service.Validate(Extended()).Success && api.Flags.Count == 0);
        });
        test("Physically unavailable display cannot select a mode or call the driver", () =>
        {
            var api = Disabled();
            for (var i = 0; i < api.State.Paths.Length; i++)
                if (api.State.Paths[i].Target.Id == 10) api.State.Paths[i].Target.Available = 0;
            var service = new WindowsDisplayService(api);
            Check(!DisplayEnableMode.TrySelect(service.Capture().Displays.Single(d => d.Id == "monitor-A"), out _));
            Check(!service.Apply(Extended(), false).Success && api.Flags.Count == 0);
        });
        test("Selecting portrait active mode returns unrotated dimensions", () =>
        {
            var display = new WindowsDisplayService(new DisabledApi()).Capture().Displays[0]
                with { Width = 2160, Height = 3840, Rotation = 90 };
            Check(DisplayEnableMode.Select(display) == new DisplayMode(3840, 2160, 60));
        });
        test("Inactive garbage mode indices are ignored and unknown timing stays driver resolved", () =>
        {
            var state = Fixture(); state.Outputs[2] = state.Outputs[2] with { Modes = [new(1280, 720, 60)] };
            state.Paths[2].Source.ModeIndex = state.Paths[2].Target.ModeIndex = 98765;
            state.Paths[3].Source.ModeIndex = state.Paths[3].Target.ModeIndex = 98765;
            var plan = CcdLogic.Build(state, Profile(
                new("monitor-A", true, true, 0, 0, 3840, 2160, 0, 60),
                new("monitor-C", true, false, 3840, 0, 1280, 720, 0, 60)));
            Check(plan.Paths.Single(p => p.Target.Id == 12).Target.ModeIndex == uint.MaxValue);
        });
    }

    private static DisabledApi Disabled()
    {
        var api = new DisabledApi();
        Success(new WindowsDisplayService(api).Apply(OnlyB(), false));
        api.Flags.Clear(); api.Plans.Clear();
        return api;
    }

    private static void Success(OperationResult result)
    { if (!result.Success) throw new Exception(result.Message); }
    private static DisplayProfile OnlyB() => Profile(new DisplayTarget("monitor-B", true, true, 0, 0, 1920, 1080, 0, 60));
    private static DisplayProfile Extended() => Profile(
        new("monitor-B", true, true, 0, 0, 1920, 1080, 0, 60),
        new("monitor-A", true, false, 1920, 0, 3840, 2160, 0, 60));

    // Stateful OS boundary: disabling removes the source/target modes and GDI modes.
    // Readback supplies driver-resolved target timings independently of the planner.
    private sealed class DisabledApi : ICcdApi
    {
        internal CcdState State = Fixture() with { Paths = Fixture().Paths.Take(2).ToArray(), Outputs = Fixture().Outputs.Take(2).ToArray() };
        internal readonly List<uint> Flags = [];
        internal readonly List<CcdPlan> Plans = [];
        internal bool KeepModes, RequireSolver;
        internal int StrictError = 31, SolverError;
        internal Action<CcdState>? AfterReenableApply;
        public CcdState Read() => State;
        public int Set(CcdPlan plan, uint flags)
        {
            Check(flags is 0x60 or 0x460 or 0xA0 or 0x2A0 or 0x4A0 or 0x6A0);
            Flags.Add(flags); Plans.Add(new(plan.Paths.ToArray(), plan.Modes.ToArray()));
            var reenable = plan.Paths.Any(p => !State.Paths.Any(old => CcdLogic.Active(old) && CcdLogic.Target(old) == CcdLogic.Target(p)));
            if ((flags & 0x40) != 0)
                return reenable && RequireSolver ? ((flags & 0x400) == 0 ? StrictError : SolverError) : 0;
            var paths = plan.Paths.ToArray(); var modes = plan.Modes.ToList();
            for (var i = 0; i < paths.Length; i++)
                if (paths[i].Target.ModeIndex == uint.MaxValue)
                {
                    var signal = State.Outputs.Single(o => o.Target == CcdLogic.Target(paths[i])).PreferredSignal
                        ?? Fixture().Outputs.Single(o => o.Target == CcdLogic.Target(paths[i])).PreferredSignal!.Value;
                    paths[i].Target.ModeIndex = (uint)modes.Count;
                    modes.Add(new() { Type = 2, Id = paths[i].Target.Id, AdapterId = paths[i].Target.AdapterId, Data = new() { Target = signal } });
                }
            var inactive = State.Paths.Where(p => !paths.Any(a => CcdLogic.Target(a) == CcdLogic.Target(p)))
                .Select(p => { p.Flags = 0; p.Source.ModeIndex = p.Target.ModeIndex = uint.MaxValue; return p; });
            State = State with { Paths = paths.Concat(inactive).ToArray(), Modes = modes.ToArray(),
                Outputs = State.Outputs.Select(o => !KeepModes && !paths.Any(p => CcdLogic.Target(p) == o.Target) ? o with { Modes = [] } : o).ToArray() };
            if (reenable) AfterReenableApply?.Invoke(State);
            return 0;
        }
    }
}
