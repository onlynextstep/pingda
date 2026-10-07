using PingXu.Core;
using PingXu.Windows;
using static AdapterTests;

internal static class ExplicitRefreshTests
{
    internal static void Run(Action<string, Action> test)
    {
        foreach (var inactive in new[] { true, false })
            test($"Explicit nonpreferred 180 Hz uses progressive scanline (inactive={inactive})", () =>
            {
                var api = new ExplicitRefreshApi();
                var index = inactive ? 2 : 1;
                var output = api.Inner.State.Outputs[index];
                api.Inner.State.Outputs[index] = output with { Modes = [new(1920, 1080, 60), new(1920, 1080, 180)] };
                var profile = Profile(new DisplayTarget(output.Id, true, true, 0, 0, 1920, 1080, 0, 180));
                var beforePaths = SnapshotCodec.Bytes(api.Read().Paths);
                var beforeModes = SnapshotCodec.Bytes(api.Read().Modes);
                var result = new WindowsDisplayService(api).Validate(profile);
                if (!result.Success) throw new Exception(result.Message);
                var plan = CcdLogic.Build(api.Read(), profile);
                var target = plan.Paths.Single().Target;
                Check(target.ModeIndex == uint.MaxValue); // Let Windows resolve real timing, not fabricated clocks.
                Check(target.RefreshRate.Numerator == 180 && target.RefreshRate.Denominator == 1);
                Check(target.ScanLineOrdering == 1);
                Check(api.Flags.SequenceEqual(new uint[] { 0x60 }));
                Check(beforePaths.SequenceEqual(SnapshotCodec.Bytes(api.Read().Paths)));
                Check(beforeModes.SequenceEqual(SnapshotCodec.Bytes(api.Read().Modes)));
            });
    }

    // Reproduces the observed NVIDIA validation boundary: explicit new rate + unspecified scanline -> 87.
    private sealed class ExplicitRefreshApi : ICcdApi
    {
        internal readonly FakeApi Inner = new();
        internal readonly List<uint> Flags = [];
        public CcdState Read() => Inner.Read();
        public int Set(CcdPlan plan, uint flags)
        {
            Flags.Add(flags);
            if ((flags & 0x40) == 0) throw new Exception("Validation test must never apply");
            if (plan.Paths.Any(p => p.Target.ModeIndex == uint.MaxValue &&
                p.Target.RefreshRate.Numerator > 0 && p.Target.ScanLineOrdering == 0)) return 87;
            return Inner.Set(plan, flags);
        }
    }
}
