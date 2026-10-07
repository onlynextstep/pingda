using PingXu.Core;
using PingXu.Windows;

internal static class ReenableDiagnostics
{
    internal static int Run(string snapshotPath, string profilePath, bool live)
    {
        var snapshotText = File.ReadAllText(snapshotPath); var profileText = File.ReadAllText(profilePath);
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<DesktopSnapshot>(snapshotText)!;
        var saved = SnapshotCodec.Decode(snapshot.NativeData);
        var profile = ProfileStore.Parse(profileText).Single();
        var plan = CcdLogic.Build(saved, profile);
        foreach (var p in saved.Paths)
            Console.WriteLine($"Captured route {p.Source.Id}->{p.Target.Id} active={CcdLogic.Active(p)} available={p.Target.Available} mode={p.Source.ModeIndex}/{p.Target.ModeIndex} flags={p.Flags:X} status={p.Source.StatusFlags:X}/{p.Target.StatusFlags:X} rotation={p.Target.Rotation} scaling={p.Target.Scaling} scan={p.Target.ScanLineOrdering} hz={p.Target.RefreshRate.Numerator}/{p.Target.RefreshRate.Denominator}");
        foreach (var p in plan.Paths)
        {
            var s = plan.Modes[p.Source.ModeIndex].Data.Source;
            Console.WriteLine($"Planned {p.Source.Id}->{p.Target.Id} source={s.Width}x{s.Height}@({s.Position.X},{s.Position.Y}) pixelFormat={s.PixelFormat} targetMode={p.Target.ModeIndex} scaling={p.Target.Scaling} scan={p.Target.ScanLineOrdering}");
            if (p.Target.ModeIndex != uint.MaxValue)
            {
                var t = plan.Modes[p.Target.ModeIndex].Data.Target;
                Console.WriteLine($"Timing {t.ActiveSize.Width}x{t.ActiveSize.Height} total={t.TotalSize.Width}x{t.TotalSize.Height} pixelRate={t.PixelRate} hsync={t.HSync.Numerator}/{t.HSync.Denominator} vsync={t.VSync.Numerator}/{t.VSync.Denominator} standard={t.VideoStandard:X} scan={t.ScanLineOrdering}");
            }
        }
        if (!live) return 0;
        var api = new LiveValidation.ValidateOnlyApi(new Win32CcdApi());
        var before = api.Read();
        if (!Same(saved, before)) throw new InvalidOperationException("Live native state differs from the supplied capture; recapture before probing.");
        try
        {
            var current = CcdLogic.Describe(before).Where(d => d.Enabled).Select(d => new DisplayTarget(d.Id, true, d.Primary,
                d.X, d.Y, d.Width, d.Height, d.Rotation, d.RefreshRate)).ToList();
            if (!BaselineAllowsProbes(api, CcdLogic.Build(before, new("current", "current", current)), Console.Out))
                return 2;
            var candidateError = Probe("reenable-baseline", plan);
            var index = Array.FindIndex(plan.Paths, p => !before.Paths.Any(old => CcdLogic.Active(old) && CcdLogic.Target(old) == CcdLogic.Target(p)));
            if (index < 0) throw new InvalidOperationException("No reenabled target in candidate.");
            foreach (var route in before.Paths.Where(p => CcdLogic.Target(p) == CcdLogic.Target(plan.Paths[index]) && p.Target.Available != 0))
            {
                if (plan.Paths.Where((_, i) => i != index).Any(p => CcdLogic.Source(p) == CcdLogic.Source(route))) continue;
                if (CcdLogic.Source(route) == CcdLogic.Source(plan.Paths[index])) continue;
                var changed = Copy(plan);
                changed.Paths[index].Source.Id = route.Source.Id; changed.Paths[index].Source.AdapterId = route.Source.AdapterId;
                ref var mode = ref changed.Modes[changed.Paths[index].Source.ModeIndex];
                mode.Id = route.Source.Id; mode.AdapterId = route.Source.AdapterId;
                Probe($"route-only/source-{route.Source.Id}", changed);
            }
            var timing = Copy(plan); timing.Paths[index].Target.ModeIndex = uint.MaxValue;
            Probe("timing-only/driver-resolved", Compact(timing));
            foreach (var scale in new uint[] { 1, 2, 128 })
            {
                if (plan.Paths[index].Target.Scaling == scale) continue;
                var changed = Copy(plan); changed.Paths[index].Target.Scaling = scale;
                Probe($"scaling-only/{scale}", changed);
            }
            return candidateError == 0 ? 0 : 1;
        }
        finally
        {
            if (!Same(before, api.Read())) throw new InvalidOperationException("Native state changed during strict validation; no restore attempted.");
            if (snapshotText != File.ReadAllText(snapshotPath) || profileText != File.ReadAllText(profilePath))
                throw new InvalidOperationException("Input file changed during probe.");
            Console.WriteLine("Input files and native path/mode bytes unchanged. Strict validation only.");
        }
        int Probe(string label, CcdPlan candidate)
        {
            // Independently constructed parameter probes; never enable ALLOW_CHANGES after error 5.
            var error = api.Set(Copy(candidate), 0x60);
            Console.WriteLine($"{label}: strict Windows error {error}");
            return error;
        }
    }
    internal static bool BaselineAllowsProbes(ICcdApi api, CcdPlan baseline, TextWriter output)
    {
        var error = api.Set(Copy(baseline), 0x60);
        output.WriteLine($"current-baseline: strict Windows error {error}");
        if (error == 0) return true;
        output.WriteLine("INCONCLUSIVE: current topology validation failed in this execution context. Candidate parameters cannot be distinguished; no further probes.");
        return false;
    }
    private static bool Same(CcdState a, CcdState b) => SnapshotCodec.Bytes(a.Paths).SequenceEqual(SnapshotCodec.Bytes(b.Paths)) &&
        SnapshotCodec.Bytes(a.Modes).SequenceEqual(SnapshotCodec.Bytes(b.Modes));
    private static CcdPlan Copy(CcdPlan p) => new(p.Paths.ToArray(), p.Modes.ToArray());
    private static CcdPlan Compact(CcdPlan p)
    {
        var paths = p.Paths.ToArray(); var modes = new List<ModeInfo>();
        for (var i = 0; i < paths.Length; i++)
        {
            var src = p.Modes[paths[i].Source.ModeIndex]; paths[i].Source.ModeIndex = (uint)modes.Count; modes.Add(src);
            if (paths[i].Target.ModeIndex == uint.MaxValue) continue;
            var dst = p.Modes[paths[i].Target.ModeIndex]; paths[i].Target.ModeIndex = (uint)modes.Count; modes.Add(dst);
        }
        return new(paths, modes.ToArray());
    }
}
