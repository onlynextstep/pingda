using PingXu.Windows;
using static AdapterTests;

internal static class PlanningRegressionTests
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (var missing in new[] { true, false })
        {
            test($"Disabled nonprimary {(missing ? "missing" : "disconnected")} preset targets do not block the remaining display", () =>
            {
                var api = OfflineApi();
                var profile = Profile(
                    new("monitor-A", true, true, 0, 0, 3840, 2160, 0, 60),
                    new(missing ? "absent-ultrawide-1" : "monitor-C", false, false, -3440, 0, 3440, 1440, 0, 180),
                    new("absent-ultrawide-2", false, false, -6880, 0, 3440, 1440, 0, 180));
                var original = profile.Displays.ToArray();
                var result = new WindowsDisplayService(api).Validate(profile);
                if (!result.Success) throw new Exception(result.Message);
                Check(api.Flags.SequenceEqual(new uint[] { 0x60 }));
                Check(profile.Displays.SequenceEqual(original));
                var plan = CcdLogic.Build(api.State, profile);
                Check(plan.Paths.Length == 1 && plan.Paths[0].Target.Id == 10);
            });
            foreach (var (enabled, primary) in new[] { (true, false), (false, true), (true, true) })
                test($"Reject {(missing ? "missing" : "disconnected")} target enabled={enabled} primary={primary} without degrading the preset", () =>
                {
                    var api = OfflineApi();
                    var profile = Profile(
                        new("monitor-A", true, true, 0, 0, 3840, 2160, 0, 60),
                        new(missing ? "absent" : "monitor-C", enabled, primary, -1920, 0, 1920, 1080, 0, 60));
                    var original = profile.Displays.ToArray();
                    Check(!new WindowsDisplayService(api).Validate(profile).Success);
                    Check(api.Flags.Count == 0 && profile.Displays.SequenceEqual(original));
                });
        }
        test("Enabling an output uses a free source before displacing an active route", () =>
        {
            var state = Fixture();
            var alternate = state.Paths[0]; alternate.Flags = 0; alternate.Source.Id = 3;
            alternate.Source.ModeIndex = alternate.Target.ModeIndex = CcdLogic.InvalidIndex;
            state = state with { Paths = state.Paths.Append(alternate).ToArray() };
            var plan = CcdLogic.Build(state, Profile(
                new("monitor-A", true, true, 0, 0, 3840, 2160, 0, 60),
                new("monitor-C", true, false, 3840, 0, 1920, 1080, 0, 60)));
            var activeSource = plan.Paths.Single(p => p.Target.Id == 10).Source.Id;
            if (activeSource != 0) throw new Exception($"Active monitor-A was unnecessarily moved from source 0 to {activeSource}; source 2 was free for monitor-C.");
            Check(plan.Paths.Single(p => p.Target.Id == 12).Source.Id == 2);
        });
        test("Live validation reports both portrait failures, preserves the exact request and reads back", () =>
        {
            var api = new RecordingValidationApi(); var output = new StringWriter();
            Check(LiveValidation.Validate(api, output) == 1);
            Check(output.ToString().Contains("Portrait extended") && output.ToString().Contains("Portrait single output"));
            Check(output.ToString().Split('\n').Where(line => line.StartsWith("Portrait ")).All(line => line.Contains("False")));
            Check(output.ToString().Contains("Native readback unchanged"));
            Check(api.Plans.Count == 6 && api.Flags.SequenceEqual(new uint[] { 0x60, 0x60, 0x60, 0x460, 0x60, 0x460 }));
            foreach (var plan in api.Plans.Skip(2))
            {
                var path = plan.Paths.Single(p => p.Target.Id == 10);
                var source = plan.Modes[path.Source.ModeIndex].Data.Source;
                Check(source.Width == 3840 && source.Height == 2160 && path.Target.Rotation == 2);
                Check(path.Target.RefreshRate.Numerator == 60000 && path.Target.RefreshRate.Denominator == 1001);
            }
        });
        test("Live validation preserves baseline error 5 without inventing error 31", () =>
        {
            var api = new RecordingValidationApi { ValidationError = 5 }; var output = new StringWriter();
            Check(LiveValidation.Validate(api, output) == 1);
            Check(api.Plans.Count == 1 && output.ToString().Contains("Windows 错误 5"));
            Check(!output.ToString().Contains("Windows 错误 31"));
            Check(output.ToString().Contains("Native readback unchanged"));
        });
        test("Live validation reports success only when both portrait solver validations succeed without native drift", () =>
        {
            var api = new RecordingValidationApi { SolverError = 0 }; var output = new StringWriter();
            Check(LiveValidation.Validate(api, output) == 0);
            Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0x60, 0x60, 0x460, 0x60, 0x460 }));
            Check(output.ToString().Split('\n').Where(line => line.StartsWith("Portrait ")).All(line => line.Contains("True")));
            Check(output.ToString().Contains("Native readback unchanged"));
            foreach (var plan in api.Plans.Skip(2))
            {
                var p = plan.Paths.Single(p => p.Target.Id == 10);
                var source = plan.Modes[p.Source.ModeIndex].Data.Source;
                Check(source.Width == 3840 && source.Height == 2160 && p.Target.Rotation == 2);
            }
        });
        test("Live validation fails on final native drift even when every validation succeeds", () =>
        {
            var api = new RecordingValidationApi { PortraitError = 0, DriftAfterValidation = true };
            var output = new StringWriter();
            Check(LiveValidation.Validate(api, output) == 1);
            Check(output.ToString().Contains("Native state changed") && !output.ToString().Contains("Native readback unchanged"));
            Check(api.Plans.Count == 4 && api.Flags.All(f => f == 0x60));
        });
        test("Live guard permits only strict or solver validation and rejects every apply flag", () =>
        {
            var api = new RecordingValidationApi(); var guard = new LiveValidation.ValidateOnlyApi(api);
            var plan = CcdLogic.Build(Fixture(), One());
            foreach (var flags in new uint[] { 0xA0, 0x2A0, 0x4A0, 0x6A0, 0x1060, 0x1460, 0x560 })
                Throws(() => guard.Set(plan, flags));
            Check(api.Plans.Count == 0);
            Check(guard.Set(plan, 0x60) == 31 && guard.Set(plan, 0x460) == 31);
            Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0x460 }));
        });
    }

    private static FakeApi OfflineApi()
    {
        var api = new FakeApi();
        for (var i = 0; i < api.State.Paths.Length; i++)
            if (api.State.Paths[i].Target.Id == 12) api.State.Paths[i].Target.Available = 0;
        return api;
    }

    private sealed class RecordingValidationApi : ICcdApi
    {
        private readonly CcdState state = Fixture();
        internal readonly List<CcdPlan> Plans = [];
        internal readonly List<uint> Flags = [];
        internal int ValidationError, PortraitError = 31;
        internal int? SolverError;
        internal bool DriftAfterValidation;
        public CcdState Read()
        {
            if (!DriftAfterValidation || Plans.Count < 4) return state;
            var paths = state.Paths.ToArray(); paths[0].Target.Scaling = 2;
            return state with { Paths = paths };
        }
        public int Set(CcdPlan plan, uint flags)
        {
            Plans.Add(plan); Flags.Add(flags);
            if (flags == 0x460 && SolverError is { } error) return error;
            return ValidationError != 0 ? ValidationError : plan.Paths.Any(p => p.Target.Rotation == 2) ? PortraitError : 0;
        }
    }
}
