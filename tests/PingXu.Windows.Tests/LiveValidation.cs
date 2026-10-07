using PingXu.Core;
using PingXu.Windows;

// Opt-in diagnostics: only strict/solver SDC_VALIDATE is permitted; never APPLY or persistence.
internal static class LiveValidation
{
    internal static int ValidateProfiles(string path)
    {
        var api = new ValidateOnlyApi(new Win32CcdApi());
        CcdState? before = null;
        var failed = false;
        try
        {
            var json = File.ReadAllText(path);
            var profiles = ProfileStore.Parse(json);
            before = api.Read();
            var service = new WindowsDisplayService(api);
            foreach (var profile in profiles)
            {
                var result = service.Validate(profile);
                Console.WriteLine($"Preset {profile.Name}: {result.Success} {result.Message}");
                failed |= !result.Success;
            }
            if (File.ReadAllText(path) != json) throw new InvalidOperationException("Preset file changed during validation.");
            Console.WriteLine("Preset file unchanged.");
        }
        catch (Exception e) { failed = true; Console.WriteLine(e.GetBaseException().Message); }
        finally
        {
            if (before is not null)
                try
                {
                    CheckUnchanged(before, api.Read());
                    Console.WriteLine("Native readback unchanged (all captured path/mode bytes).");
                }
                catch (Exception e) { failed = true; Console.WriteLine(e.GetBaseException().Message); }
        }
        return failed ? 1 : 0;
    }

    internal static int Validate(ICcdApi inner, TextWriter output)
    {
        var api = new ValidateOnlyApi(inner);
        var service = new WindowsDisplayService(api);
        CcdState? before = null;
        var failed = false;
        try
        {
            before = api.Read();
            var displays = CcdLogic.Describe(before);
            var profile = CurrentProfile(displays);
            output.WriteLine($"Active outputs: {displays.Count(d => d.Enabled)} (extended preserves this active set).");
            var baseline = service.Validate(profile);
            output.WriteLine($"Current topology SDC_VALIDATE: {baseline.Success} {baseline.Message}");
            if (!baseline.Success) throw new InvalidOperationException("当前布局验证失败，停止后续候选验证。");
            var saved = SnapshotCodec.Decode(SnapshotCodec.Encode(before));
            var snapshotError = api.Set(CcdLogic.Rebind(saved, api.Read()), 0x60);
            output.WriteLine($"Rebound native snapshot SDC_VALIDATE: Windows error {snapshotError}");
            NativeErrors.Check(snapshotError, "只验证原生恢复方案");
            var selected = displays.Where(d => d.Enabled).OrderByDescending(d => (long)d.Width * d.Height).First();
            foreach (var only in new[] { false, true })
            {
                var result = service.Validate(PortraitProfile(profile, selected.Id, only));
                output.WriteLine($"Portrait {(only ? "single output" : "extended")} SDC_VALIDATE: {result.Success} {result.Message}");
                failed |= !result.Success;
            }
        }
        catch (Exception e) { failed = true; output.WriteLine(e.GetBaseException().Message); }
        finally
        {
            if (before is not null)
                try
                {
                    CheckUnchanged(before, api.Read());
                    output.WriteLine("Native readback unchanged (all captured path/mode bytes).");
                }
                catch (Exception e) { failed = true; output.WriteLine(e.GetBaseException().Message); }
        }
        return failed ? 1 : 0;
    }

    private static DisplayProfile CurrentProfile(List<DisplayInfo> displays) => new("validate", "只验证", displays.Select(d =>
        new DisplayTarget(d.Id, d.Enabled, d.Primary, d.X, d.Y, d.Width, d.Height, d.Rotation, d.RefreshRate)).ToList());

    private static DisplayProfile PortraitProfile(DisplayProfile profile, string selectedId, bool only)
    {
        var targets = profile.Displays.Select(d => d.Id == selectedId
            ? d with { Width = d.Height, Height = d.Width, Rotation = (d.Rotation + 90) % 360, Primary = only || d.Primary }
            : d with { Enabled = !only && d.Enabled, Primary = !only && d.Primary }).ToList();
        var x = -targets.Where(d => d.Enabled && !d.Primary).Sum(d => d.Width);
        for (var i = 0; i < targets.Count; i++)
            if (targets[i].Enabled)
            {
                var d = targets[i]; targets[i] = d with { X = d.Primary ? 0 : x, Y = 0 };
                if (!d.Primary) x += d.Width;
            }
        return profile with { Displays = targets };
    }

    private static void CheckUnchanged(CcdState before, CcdState after)
    {
        // Compare native bytes, not timestamped SnapshotCodec envelopes.
        if (!SnapshotCodec.Bytes(before.Paths).SequenceEqual(SnapshotCodec.Bytes(after.Paths)) ||
            !SnapshotCodec.Bytes(before.Modes).SequenceEqual(SnapshotCodec.Bytes(after.Modes)))
            throw new InvalidOperationException("Native state changed during validation; no restore attempted.");
    }

    internal sealed class ValidateOnlyApi(ICcdApi inner) : ICcdApi
    {
        public CcdState Read() => inner.Read();
        public int Set(CcdPlan plan, uint flags)
        {
            if (flags is not (0x60 or 0x460))
                throw new InvalidOperationException("Live probe permits only strict or solver SDC_VALIDATE.");
            return inner.Set(plan, flags);
        }
    }

    internal static int Diagnose()
    {
        var api = new ValidateOnlyApi(new Win32CcdApi());
        try
        {
            var state = api.Read();
            var displays = CcdLogic.Describe(state);
            var profile = new DisplayProfile("diagnose", "只验证", displays.Select(d =>
                new DisplayTarget(d.Id, d.Enabled, d.Primary, d.X, d.Y, d.Width, d.Height, d.Rotation, d.RefreshRate)).ToList());
            var selected = displays.Where(d => d.Enabled).OrderByDescending(d => (long)d.Width * d.Height).First();
            var endpoint = state.Outputs.Single(o => o.Id == selected.Id).Target;
            foreach (var path in state.Paths.Where(CcdLogic.Active))
            {
                var source = CcdLogic.Mode(state, path, true).Data.Source;
                var signal = CcdLogic.Mode(state, path, false).Data.Target;
                Console.WriteLine($"Active source={path.Source.Id} target={path.Target.Id} desktop={source.Width}x{source.Height}@({source.Position.X},{source.Position.Y}) rotation={path.Target.Rotation} scaling={path.Target.Scaling} rate={path.Target.RefreshRate.Numerator}/{path.Target.RefreshRate.Denominator} signal={signal.ActiveSize.Width}x{signal.ActiveSize.Height} total={signal.TotalSize.Width}x{signal.TotalSize.Height} pixelRate={signal.PixelRate}");
            }
            Probe("current", CcdLogic.Build(state, profile));
            Probe("snapshot", CcdLogic.Rebind(state, state));
            var native = CcdLogic.Rebind(state, state);
            var selectedIndex = Array.FindIndex(native.Paths, p => CcdLogic.Target(p) == endpoint);
            foreach (var rotation in new uint[] { 2, 3, 4 })
            {
                var rotated = Copy(native);
                rotated.Paths[selectedIndex].Target.Rotation = rotation;
                Probe($"minimal/rotation-{rotation}-only", rotated);
                if (rotation is 2 or 4)
                {
                    ref var src = ref rotated.Modes[rotated.Paths[selectedIndex].Source.ModeIndex].Data.Source;
                    (src.Width, src.Height) = (src.Height, src.Width);
                    Probe($"minimal/rotation-{rotation}-and-footprint", rotated);
                }
            }
            var footprint = Copy(native);
            ref var footprintSource = ref footprint.Modes[footprint.Paths[selectedIndex].Source.ModeIndex].Data.Source;
            (footprintSource.Width, footprintSource.Height) = (footprintSource.Height, footprintSource.Width);
            Probe("minimal/footprint-only", footprint);
            foreach (var flags in new uint[] { 0, 4 })
            {
                var counts = new Dictionary<uint, int>();
                for (uint modeIndex = 0; modeIndex < 16384; modeIndex++)
                {
                    var dm = new DevMode { Size = (ushort)System.Runtime.InteropServices.Marshal.SizeOf<DevMode>() };
                    if (!NativeMethods.EnumDisplaySettingsEx(selected.DeviceName, modeIndex, ref dm, flags)) break;
                    counts[dm.Orientation] = counts.GetValueOrDefault(dm.Orientation) + 1;
                }
                Console.WriteLine($"EnumDisplaySettingsEx flags={flags} orientations: {string.Join(", ", counts.Select(c => $"{c.Key}:{c.Value}"))}");
                var rotated = Copy(footprint); rotated.Paths[selectedIndex].Target.Rotation = 2;
                Probe($"minimal/portrait-after-enumeration-{flags}", rotated);
            }
            foreach (var size in new[] { (1920u, 1080u), (1080u, 1920u), (2160u, 3840u) })
            {
                var candidate = Copy(native);
                candidate.Paths[selectedIndex].Target.Rotation = size.Item1 < size.Item2 ? 2u : 1u;
                ref var source = ref candidate.Modes[candidate.Paths[selectedIndex].Source.ModeIndex].Data.Source;
                source.Width = size.Item1; source.Height = size.Item2;
                Probe($"size/{size}/old-timing", candidate);
                candidate.Paths[selectedIndex].Target.ModeIndex = CcdLogic.InvalidIndex;
                Probe($"size/{size}/omit-timing", Compact(candidate));
                candidate.Paths[selectedIndex].Target.RefreshRate = default;
                candidate.Paths[selectedIndex].Target.ScanLineOrdering = 0;
                Probe($"size/{size}/diagnostic-unspecified-rate-and-scan", Compact(candidate));
            }
            foreach (var only in new[] { false, true })
            {
                var targets = profile.Displays.Select(d => d.Id == selected.Id
                    ? d with { Width = d.Height, Height = d.Width, Rotation = (d.Rotation + 90) % 360, Primary = only || d.Primary }
                    : d with { Enabled = !only && d.Enabled, Primary = !only && d.Primary }).ToList();
                var x = -targets.Where(d => d.Enabled && !d.Primary).Sum(d => d.Width);
                for (var i = 0; i < targets.Count; i++)
                    if (targets[i].Enabled)
                    {
                        var d = targets[i]; targets[i] = d with { X = d.Primary ? 0 : x, Y = 0 };
                        if (!d.Primary) x += d.Width;
                    }
                var plan = CcdLogic.Build(state, profile with { Displays = targets });
                var label = only ? "single" : "extended";
                Probe($"{label}/baseline", plan);
                // H1: restore active source identities, updating matching source-mode ownership only.
                var routes = Copy(plan);
                for (var i = 0; i < routes.Paths.Length; i++)
                {
                    var old = state.Paths.Single(p => CcdLogic.Active(p) && CcdLogic.Target(p) == CcdLogic.Target(routes.Paths[i]));
                    routes.Paths[i].Source.Id = old.Source.Id;
                    routes.Paths[i].Source.AdapterId = old.Source.AdapterId;
                    ref var mode = ref routes.Modes[routes.Paths[i].Source.ModeIndex];
                    mode.Id = old.Source.Id; mode.AdapterId = old.Source.AdapterId;
                }
                Probe($"{label}/H1-active-sources", routes);
                if (only)
                    foreach (var route in state.Paths.Where(p => CcdLogic.Target(p) == endpoint && p.Target.Available != 0))
                    {
                        var alternate = Copy(plan);
                        alternate.Paths[0].Source.AdapterId = route.Source.AdapterId;
                        alternate.Paths[0].Source.Id = route.Source.Id;
                        ref var source = ref alternate.Modes[alternate.Paths[0].Source.ModeIndex];
                        source.AdapterId = route.Source.AdapterId; source.Id = route.Source.Id;
                        Probe($"{label}/H1-candidate-source-{route.Source.Id}", alternate);
                    }
                // Each following probe starts from the identical baseline and changes one target property.
                foreach (var scaling in new uint[] { 1, 2, 3, 4, 128 })
                {
                    var scaled = Copy(plan);
                    var index = Array.FindIndex(scaled.Paths, p => CcdLogic.Target(p) == endpoint);
                    scaled.Paths[index].Target.Scaling = scaling;
                    Probe($"{label}/H2-scaling-{scaling}", scaled);
                }
                var solved = Copy(plan);
                solved.Paths[Array.FindIndex(solved.Paths, p => CcdLogic.Target(p) == endpoint)].Target.ModeIndex = CcdLogic.InvalidIndex;
                Probe($"{label}/H3-omit-selected-timing", Compact(solved));
                var solvedIndex = Array.FindIndex(solved.Paths, p => CcdLogic.Target(p) == endpoint);
                foreach (var scaling in new uint[] { 2, 3, 4, 128 })
                {
                    var scaled = Copy(solved); scaled.Paths[solvedIndex].Target.Scaling = scaling;
                    Probe($"{label}/H3-omit-timing-then-scaling-{scaling}", Compact(scaled));
                }
                solved.Paths[solvedIndex].Target.ScanLineOrdering = 0;
                Probe($"{label}/H3-then-unspecified-scanline", Compact(solved));
                solved.Paths[solvedIndex].Target.Scaling = 128;
                Probe($"{label}/H3-then-preferred-scaling", Compact(solved));
            }
            var after = api.Read();
            if (!SnapshotCodec.Bytes(state.Paths).SequenceEqual(SnapshotCodec.Bytes(after.Paths)) ||
                !SnapshotCodec.Bytes(state.Modes).SequenceEqual(SnapshotCodec.Bytes(after.Modes)))
                throw new InvalidOperationException("Native state changed during validation; no restore attempted.");
            Console.WriteLine("Native readback unchanged (all captured path/mode bytes).");
            return 0;

            void Probe(string label, CcdPlan plan)
            {
                var error = api.Set(Copy(plan), 0x60);
                Console.WriteLine($"{label}: Windows error {error}; routes {string.Join(", ", plan.Paths.Select(p => $"{p.Source.Id}->{p.Target.Id}"))}");
            }
        }
        catch (Exception e) { Console.WriteLine(e.GetBaseException().Message); return 1; }
    }

    private static CcdPlan Copy(CcdPlan plan) => new(plan.Paths.ToArray(), plan.Modes.ToArray());
    private static CcdPlan Compact(CcdPlan plan)
    {
        var paths = plan.Paths.ToArray(); var modes = new List<ModeInfo>();
        for (var i = 0; i < paths.Length; i++)
        {
            var src = plan.Modes[paths[i].Source.ModeIndex];
            paths[i].Source.ModeIndex = (uint)modes.Count; modes.Add(src);
            if (paths[i].Target.ModeIndex == CcdLogic.InvalidIndex) continue;
            var dst = plan.Modes[paths[i].Target.ModeIndex];
            paths[i].Target.ModeIndex = (uint)modes.Count; modes.Add(dst);
        }
        return new(paths, modes.ToArray());
    }
}
