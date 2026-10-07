using System.Reflection;
using System.Text.Json;
using PingXu.Windows;

// File-only diagnostic. No service instance, ICcdApi, Validate, Apply or Restore calls.
// Exit 0: stable-ID desktop/native contract matches (NOT necessarily raw equality).
// Exit 1: contract differs. Exit 2: invalid arguments, unreadable or invalid snapshots.
internal static class SnapshotComparison
{
    internal static int Run(string[] paths, TextWriter output)
    {
        if (paths.Length != 2)
        {
            output.WriteLine("Usage: --compare-snapshots before.json after.json");
            return 2;
        }
        try { return CompareJson(File.ReadAllText(paths[0]), File.ReadAllText(paths[1]), output); }
        catch (Exception e)
        {
            output.WriteLine($"Snapshot comparison ERROR: {e.GetBaseException().Message}");
            return 2;
        }
    }

    internal static int CompareJson(string before, string after, TextWriter output)
    {
        var a = Decode(before); var b = Decode(after);
        Raw("Paths", SnapshotCodec.Bytes(a.Paths), SnapshotCodec.Bytes(b.Paths), output);
        Raw("Modes", SnapshotCodec.Bytes(a.Modes), SnapshotCodec.Bytes(b.Modes), output);
        // Positional raw differences include inactive route candidates; no discarded bytes.
        for (var i = 0; i < Math.Min(a.Paths.Length, b.Paths.Length); i++)
            Differences($"RAW Path[{i}]", Fields(a.Paths[i]), Fields(b.Paths[i]), output);
        for (var i = 0; i < Math.Min(a.Modes.Length, b.Modes.Length); i++)
        {
            var x = a.Modes[i]; var y = b.Modes[i];
            Differences($"RAW Mode[{i}] header", Fields(new { x.Type, x.Id, x.AdapterId }),
                Fields(new { y.Type, y.Id, y.AdapterId }), output);
        }
        var aa = CcdLogic.Describe(a).Where(d => d.Enabled).ToDictionary(d => d.Id, CcdLogic.IdentityComparer);
        var bb = CcdLogic.Describe(b).Where(d => d.Enabled).ToDictionary(d => d.Id, CcdLogic.IdentityComparer);
        var match = true;
        foreach (var id in aa.Keys.Union(bb.Keys, CcdLogic.IdentityComparer).OrderBy(id => id, CcdLogic.IdentityComparer))
        {
            output.WriteLine($"DISPLAY {id}");
            if (!aa.TryGetValue(id, out var x) || !bb.TryGetValue(id, out var y))
            {
                output.WriteLine($"  MISMATCH enabled: {aa.ContainsKey(id)} -> {bb.ContainsKey(id)}");
                match = false; continue;
            }
            var old = ActivePath(a, id); var now = ActivePath(b, id);
            output.WriteLine($"  CHANNEL {x.DeviceName} -> {y.DeviceName}; source {Endpoint(old.Source.AdapterId, old.Source.Id)} -> {Endpoint(now.Source.AdapterId, now.Source.Id)}; target {Endpoint(old.Target.AdapterId, old.Target.Id)} -> {Endpoint(now.Target.AdapterId, now.Target.Id)}; mode indices {old.Source.ModeIndex}/{old.Target.ModeIndex} -> {now.Source.ModeIndex}/{now.Target.ModeIndex}");
            var desktopA = Fields(new { x.Connected, x.Enabled, x.Primary, x.X, x.Y, x.Width, x.Height, x.Rotation, x.RefreshRate });
            var desktopB = Fields(new { y.Connected, y.Enabled, y.Primary, y.X, y.Y, y.Width, y.Height, y.Rotation, y.RefreshRate });
            var sameDesktop = !Differences("  MISMATCH Desktop", desktopA, desktopB, output);
            output.WriteLine($"  Desktop: {(sameDesktop ? "MATCH" : "DIFFERENT")} {x.Width}x{x.Height}/{x.Rotation}deg/{x.RefreshRate}Hz/({x.X},{x.Y}) primary={x.Primary}");
            var sourceA = CcdLogic.Mode(a, old, true); var sourceB = CcdLogic.Mode(b, now, true);
            var targetA = CcdLogic.Mode(a, old, false); var targetB = CcdLogic.Mode(b, now, false);
            var sameNative = !Differences("  MISMATCH Source", Fields(sourceA.Data.Source), Fields(sourceB.Data.Source), output);
            sameNative &= !Differences("  MISMATCH TargetSignal", Fields(targetA.Data.Target), Fields(targetB.Data.Target), output);
            sameNative &= !Differences("  MISMATCH Path", PathContract(old), PathContract(now), output);
            // Check full union payloads as well as named fields, so hidden/reserved data is not silently ignored.
            sameNative &= Payload("Source union", sourceA.Data, sourceB.Data, output);
            sameNative &= Payload("Target union", targetA.Data, targetB.Data, output);
            output.WriteLine($"  Native payload/path contract: {(sameNative ? "MATCH" : "DIFFERENT")} (full signal/union bytes, source size/position/pixel format, scaling, technology, rational Hz, scanline, availability/status/flags)");
            match &= sameDesktop && sameNative;
        }
        try
        {
            // Reuse exactly the production restore signal check, without constructing a native backend.
            var method = typeof(WindowsDisplayService).GetMethod("CompareNative", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new MissingMethodException("CompareNative");
            method.Invoke(null, [a, b]);
            output.WriteLine("CompareNative: MATCH");
        }
        catch (TargetInvocationException e)
        {
            output.WriteLine($"CompareNative: DIFFERENT ({e.GetBaseException().Message})");
            match = false;
        }
        output.WriteLine($"Desktop/native contract: {(match ? "MATCH" : "DIFFERENT")}; raw equality reported separately. Binding IDs/indices, channel names and inactive candidate routes are not desktop/signal equality.");
        return match ? 0 : 1;
    }

    private static CcdState Decode(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        // Accept capture-live DesktopSnapshot or its original NativeData envelope.
        var state = SnapshotCodec.Decode(root.TryGetProperty("NativeData", out var native) ? native.GetString()! : json);
        if (state.Outputs.Any(o => string.IsNullOrWhiteSpace(o.Id)) ||
            state.Outputs.GroupBy(o => o.Id, CcdLogic.IdentityComparer).Any(g => g.Count() != 1))
            throw new InvalidOperationException("Snapshot stable IDs are missing or ambiguous.");
        return state;
    }
    private static PathInfo ActivePath(CcdState state, string id)
    {
        var target = state.Outputs.Single(o => CcdLogic.IdentityComparer.Equals(o.Id, id)).Target;
        return state.Paths.Single(p => CcdLogic.Active(p) && CcdLogic.Target(p) == target);
    }
    private static string Endpoint(Luid adapter, uint id) => $"{adapter.High:X8}:{adapter.Low:X8}/{id}";
    private static Dictionary<string, string> PathContract(PathInfo p) => Fields(new
    {
        p.Flags, SourceStatus = p.Source.StatusFlags, TargetStatus = p.Target.StatusFlags,
        p.Target.Available, p.Target.Rotation, p.Target.Scaling, p.Target.OutputTechnology,
        p.Target.RefreshRate, p.Target.ScanLineOrdering
    });
    private static bool Payload(string name, ModeUnion a, ModeUnion b, TextWriter output)
    {
        var x = SnapshotCodec.Bytes(new[] { a }); var y = SnapshotCodec.Bytes(new[] { b });
        if (x.SequenceEqual(y)) return true;
        Raw($"MISMATCH {name}", x, y, output); return false;
    }
    private static void Raw(string name, byte[] a, byte[] b, TextWriter output)
    {
        var offsets = Enumerable.Range(0, Math.Max(a.Length, b.Length))
            .Where(i => i >= a.Length || i >= b.Length || a[i] != b[i]).ToArray();
        output.WriteLine($"RAW {name}: {(offsets.Length == 0 ? "MATCH" : "DIFFERENT")}; bytes={a.Length}->{b.Length}; differing offsets={offsets.Length}");
        if (offsets.Length != 0)
            output.WriteLine("  " + string.Join(", ", offsets.Select(i => $"{i}:{(i < a.Length ? a[i].ToString("X2") : "--")}->{(i < b.Length ? b[i].ToString("X2") : "--")}")));
    }
    private static bool Differences(string prefix, Dictionary<string, string> a, Dictionary<string, string> b, TextWriter output)
    {
        var changed = false;
        foreach (var key in a.Keys.Union(b.Keys))
        {
            var x = a.GetValueOrDefault(key, "<missing>"); var y = b.GetValueOrDefault(key, "<missing>");
            if (x == y) continue;
            output.WriteLine($"{prefix}.{key}: {x} -> {y}"); changed = true;
        }
        return changed;
    }
    private static Dictionary<string, string> Fields(object value)
    {
        var result = new Dictionary<string, string>();
        void Walk(object item, string prefix)
        {
            var type = item.GetType();
            if (type.IsPrimitive || item is string)
            { result[prefix] = Convert.ToString(item, System.Globalization.CultureInfo.InvariantCulture)!; return; }
            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                Walk(f.GetValue(item)!, prefix.Length == 0 ? f.Name : prefix + "." + f.Name);
            foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                Walk(p.GetValue(item)!, prefix.Length == 0 ? p.Name : prefix + "." + p.Name);
        }
        Walk(value, ""); return result;
    }
}
