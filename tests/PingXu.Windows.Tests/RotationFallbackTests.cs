using PingXu.Core;
using PingXu.Windows;
using static AdapterTests;

internal static class RotationFallbackTests
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (var error in new[] { 31, 1610 })
            foreach (var angle in new[] { 90, 270 })
                test($"Rotation {angle} strict error {error} validates with solver only", () =>
                {
                    var api = new SolverApi { StrictError = error };
                    var result = new WindowsDisplayService(api).Validate(Portrait(angle));
                    Check(result.Success);
                    Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0x460 }));
                    Check(api.Inner.ReadCount == 1);
                    SamePlans(api.Plans);
                });
        foreach (var error in new[] { 31, 1610 })
            test($"Unchanged rotation never falls back for error {error}", () =>
            {
                var api = new SolverApi { StrictError = error };
                Check(!new WindowsDisplayService(api).Apply(Landscape(), false).Success);
                Check(api.Flags.SequenceEqual(new uint[] { 0x60 }));
            });
        test("Already portrait is compared with the current physical device, not with zero degrees", () =>
        {
            var api = new SolverApi();
            api.Inner.State.Paths[0].Target.Rotation = 2;
            api.Inner.State.Modes[0].Data.Source.Width = 3840;
            api.Inner.State.Modes[0].Data.Source.Height = 2160;
            Check(!new WindowsDisplayService(api).Validate(Portrait(90)).Success);
            Check(api.Flags.SequenceEqual(new uint[] { 0x60 }));
        });
        test("Disabled target angle does not authorize solver", () =>
        {
            var api = new SolverApi(); var profile = Landscape();
            profile.Displays.Add(new("monitor-C", false, false, 0, 0, 1080, 1920, 90, 60));
            Check(!new WindowsDisplayService(api).Validate(profile).Success);
            Check(api.Flags.SequenceEqual(new uint[] { 0x60 }));
        });
        foreach (var error in new[] { 5, 87, 50 })
            test($"Rotation strict error {error} never falls back", () =>
            {
                var api = new SolverApi { StrictError = error };
                Check(!new WindowsDisplayService(api).Apply(Portrait(90), true).Success);
                Check(api.Flags.SequenceEqual(new uint[] { 0x60 }));
            });
        foreach (var error in new[] { 31, 1610, 5 })
            test($"Solver failure {error} never applies", () =>
            {
                var api = new SolverApi { SolverError = error };
                var result = new WindowsDisplayService(api).Apply(Portrait(90), false);
                Check(!result.Success && result.Message.Contains($"Windows 错误 {error}"));
                Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0x460 }));
            });
        foreach (var persist in new[] { false, true })
        {
            test($"Solver apply persist={persist} uses matching flags and strict successful readback", () =>
            {
                var api = new SolverApi(); var requested = Portrait(90);
                // A mutable caller must not be able to replace the readback contract during validation.
                api.AfterStrict = () => requested.Displays.Clear();
                var result = new WindowsDisplayService(api).Apply(requested, persist);
                Check(result.Success);
                Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0x460, persist ? 0x6A0u : 0x4A0u }));
                Check(api.Inner.ReadCount == 3);
                SamePlans(api.Plans);
            });
            test($"Strict success persist={persist} never enables solver", () =>
            {
                var api = new SolverApi { StrictError = 0 };
                Check(new WindowsDisplayService(api).Apply(Portrait(90), persist).Success);
                Check(api.Flags.SequenceEqual(new uint[] { 0x60, persist ? 0x2A0u : 0xA0u }));
            });
            test($"Restore portrait persist={persist} uses solver and requires exact native readback", () =>
            {
                var api = new SolverApi(); var saved = Fixture();
                saved.Paths[0].Target.Rotation = 2;
                saved.Modes[0].Data.Source.Width = 3840; saved.Modes[0].Data.Source.Height = 2160;
                Check(new WindowsDisplayService(api).Restore(new([], SnapshotCodec.Encode(saved)), persist).Success);
                Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0x460, persist ? 0x6A0u : 0x4A0u }));
                SamePlans(api.Plans);
            });
        }
        foreach (var field in new[] { "pixel-clock", "scaling", "path-rate", "pixel-format", "scanline", "total-size" })
            test($"Solver restore rejects changed native {field} even when integer desktop fields match", () =>
            {
                var api = new SolverApi(); var saved = Fixture();
                saved.Paths[0].Target.Rotation = 2;
                saved.Modes[0].Data.Source.Width = 3840; saved.Modes[0].Data.Source.Height = 2160;
                api.AfterApply = () =>
                {
                    var state = api.Inner.State;
                    ref var path = ref state.Paths[0];
                    ref var signal = ref state.Modes[path.Target.ModeIndex].Data.Target;
                    switch (field)
                    {
                        case "pixel-clock": signal.PixelRate++; break;
                        case "scaling": path.Target.Scaling = 2; break;
                        case "path-rate": path.Target.RefreshRate = new() { Numerator = 60, Denominator = 1 }; break;
                        case "pixel-format": state.Modes[path.Source.ModeIndex].Data.Source.PixelFormat = 3; break;
                        case "scanline": path.Target.ScanLineOrdering = 2; break;
                        case "total-size": signal.TotalSize.Width++; break;
                    }
                };
                var result = new WindowsDisplayService(api).Restore(new([], SnapshotCodec.Encode(saved)), true);
                Check(!result.Success && result.Message.Contains("回读") && result.Message.Contains("原生信号"));
                Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0x460, 0x6A0 }));
            });
        foreach (var error in new[] { 31, 1610, 5 })
            test($"Restore unchanged rotation error {error} remains strict", () =>
            {
                var api = new SolverApi { StrictError = error };
                Check(!new WindowsDisplayService(api).Restore(new([], SnapshotCodec.Encode(Fixture()))).Success);
                Check(api.Flags.SequenceEqual(new uint[] { 0x60 }));
            });
        test("Restore changed rotation solver failure never applies", () =>
        {
            var api = new SolverApi { StrictError = 1610, SolverError = 31 }; var saved = Fixture();
            saved.Paths[0].Target.Rotation = 2;
            saved.Modes[0].Data.Source.Width = 3840; saved.Modes[0].Data.Source.Height = 2160;
            Check(!new WindowsDisplayService(api).Restore(new([], SnapshotCodec.Encode(saved))).Success);
            Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0x460 }));
        });
        foreach (var error in new[] { 31, 1610 })
            foreach (var originalPortrait in new[] { false, true })
                test($"Restore rotation change in either direction originalPortrait={originalPortrait}, strict={error}, rebinds identity", () =>
                {
                    var saved = Fixture(); var current = Fixture();
                    var portrait = originalPortrait ? saved : current;
                    portrait.Paths[0].Target.Rotation = 2;
                    portrait.Modes[0].Data.Source.Width = 3840; portrait.Modes[0].Data.Source.Height = 2160;
                    var api = new SolverApi { StrictError = error };
                    api.Inner.State = Readdress(current);
                    Check(new WindowsDisplayService(api).Restore(new([], SnapshotCodec.Encode(saved))).Success);
                    Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0x460, 0x4A0 }));
                    SamePlans(api.Plans);
                });
        test("Restore rotation access error 5 never requests solver", () =>
        {
            var api = new SolverApi { StrictError = 5 }; var saved = Fixture();
            saved.Paths[0].Target.Rotation = 2;
            saved.Modes[0].Data.Source.Width = 3840; saved.Modes[0].Data.Source.Height = 2160;
            Check(!new WindowsDisplayService(api).Restore(new([], SnapshotCodec.Encode(saved))).Success);
            Check(api.Flags.SequenceEqual(new uint[] { 0x60 }));
        });
        test("Validation buffer changes cannot replace the requested plan used by solver and readback", () =>
        {
            var api = new SolverApi();
            api.StrictBufferMutation = p => p.Modes[p.Paths[0].Source.ModeIndex].Data.Source.Width = 1920;
            Check(new WindowsDisplayService(api).Apply(Portrait(90), false).Success);
            Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0x460, 0x4A0 }));
            SamePlans(api.Plans);
        });
        foreach (var persist in new[] { false, true })
            test($"Solver apply native failure persist={persist} is visible without further retries", () =>
            {
                var api = new SolverApi(); api.Inner.ApplyError = 31;
                var result = new WindowsDisplayService(api).Apply(Portrait(90), persist);
                Check(!result.Success && result.Message.Contains("Windows 错误 31"));
                Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0x460, persist ? 0x6A0u : 0x4A0u }));
                Check(api.Inner.ReadCount == 1);
            });
        foreach (var field in new[] { "width", "height", "angle", "x", "y", "primary", "hz", "extra-enabled", "identity" })
            test($"Solver-adjusted {field} cannot report apply success", () =>
            {
                var api = new SolverApi();
                api.AfterApply = () =>
                {
                    var state = api.Inner.State;
                    var index = Array.FindIndex(state.Paths, p => p.Target.Id == 10);
                    ref var path = ref state.Paths[index];
                    ref var source = ref state.Modes[path.Source.ModeIndex].Data.Source;
                    switch (field)
                    {
                        case "width": source.Width--; break;
                        case "height": source.Height--; break;
                        case "angle": path.Target.Rotation = 4; break;
                        case "x": source.Position.X = -2161; break;
                        case "y": source.Position.Y = 1; break;
                        case "primary":
                            source.Position.X = 0;
                            var other = state.Paths.Single(p => p.Target.Id == 11);
                            state.Modes[other.Source.ModeIndex].Data.Source.Position.X = 2160;
                            break;
                        case "hz": path.Target.RefreshRate = new() { Numerator = 59, Denominator = 1 }; break;
                        case "extra-enabled": state.Paths[^1].Flags = 1; break;
                        case "identity": state.Outputs[0] = state.Outputs[0] with { Id = "changed-monitor" }; break;
                    }
                };
                var requested = Profile(
                    new("monitor-A", true, false, -2160, 0, 2160, 3840, 90, 60),
                    new("monitor-B", true, true, 0, 0, 1920, 1080, 0, 60));
                var result = new WindowsDisplayService(api).Apply(requested, false);
                Check(!result.Success && result.Message.Contains("未能确认") && !result.Message.Contains("看护"));
                Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0x460, 0x4A0 }));
            });
    }

    private static DisplayProfile Portrait(int angle) => Profile(new DisplayTarget("MONITOR-A", true, true, 0, 0, 2160, 3840, angle, 60));
    private static DisplayProfile Landscape() => Profile(new DisplayTarget("monitor-A", true, true, 0, 0, 3840, 2160, 0, 60));
    private static void SamePlans(List<CcdPlan> plans)
    {
        foreach (var plan in plans.Skip(1))
        {
            Check(SnapshotCodec.Bytes(plan.Paths).SequenceEqual(SnapshotCodec.Bytes(plans[0].Paths)));
            Check(SnapshotCodec.Bytes(plan.Modes).SequenceEqual(SnapshotCodec.Bytes(plans[0].Modes)));
        }
    }
    private sealed class SolverApi : ICcdApi
    {
        internal readonly FakeApi Inner = new();
        internal readonly List<uint> Flags = [];
        internal readonly List<CcdPlan> Plans = [];
        internal int StrictError = 31, SolverError;
        internal Action? AfterStrict, AfterApply;
        internal Action<CcdPlan>? StrictBufferMutation;
        public CcdState Read() => Inner.Read();
        public int Set(CcdPlan plan, uint flags)
        {
            Flags.Add(flags); Plans.Add(new(plan.Paths.ToArray(), plan.Modes.ToArray()));
            if (flags == 0x60) { AfterStrict?.Invoke(); StrictBufferMutation?.Invoke(plan); return StrictError; }
            if (flags == 0x460) return SolverError;
            if (flags is not (0xA0 or 0x2A0 or 0x4A0 or 0x6A0)) throw new Exception("Unexpected flags");
            var error = Inner.Set(plan, flags); AfterApply?.Invoke(); return error;
        }
    }
}
