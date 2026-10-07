using System.Text.Json;
using PingXu.Windows;
using static AdapterTests;

internal static class SnapshotComparisonTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("Offline snapshot comparison distinguishes source rebinding from raw equality", () =>
        {
            var before = Fixture(); var after = Fixture();
            for (var i = 0; i < after.Paths.Length; i++) after.Paths[i].Source.Id += 10;
            for (var i = 0; i < after.Modes.Length; i++)
                if (after.Modes[i].Type == 1) after.Modes[i].Id += 10;
            after = after with { Outputs = after.Outputs.Select(o => o with { DeviceName = "rebound-channel" }).ToArray() };
            var a = Json(before); var b = Json(after); var output = new StringWriter();
            Check(SnapshotComparison.CompareJson(a, b, output) == 0);
            Check(output.ToString().Contains("RAW Paths: DIFFERENT") && output.ToString().Contains("Source.Id: 0 -> 10"));
            Check(output.ToString().Contains("Desktop/native contract: MATCH") && output.ToString().Contains("CompareNative: MATCH"));
        });
        foreach (var field in new[] { "width", "height", "rotation", "x", "y", "primary", "integerHz", "enabled", "stableId",
            "pixelRate", "totalSize", "rationalHz", "scaling", "pixelFormat", "scanline", "technology", "sourceUnionTail" })
            test($"Offline snapshot comparison rejects changed {field}", () =>
            {
                var before = Fixture(); var after = Fixture();
                switch (field)
                {
                    case "width": after.Modes[0].Data.Source.Width--; break;
                    case "height": after.Modes[0].Data.Source.Height--; break;
                    case "rotation": after.Paths[0].Target.Rotation = 2; break;
                    case "x": after.Modes[0].Data.Source.Position.X--; break;
                    case "y": after.Modes[0].Data.Source.Position.Y++; break;
                    case "primary":
                        after.Modes[0].Data.Source.Position.X = 0;
                        after.Modes[2].Data.Source.Position.X = 3840; break;
                    case "integerHz": after.Paths[0].Target.RefreshRate = new() { Numerator = 59, Denominator = 1 }; break;
                    case "enabled": after.Paths[0].Flags = 0; break;
                    case "stableId": after.Outputs[0] = after.Outputs[0] with { Id = "different-physical-monitor" }; break;
                    case "pixelRate": after.Modes[1].Data.Target.PixelRate++; break;
                    case "totalSize": after.Modes[1].Data.Target.TotalSize.Width++; break;
                    // Same integer Hz, different exact rational value: must still fail.
                    case "rationalHz": after.Paths[0].Target.RefreshRate.Numerator++; break;
                    case "scaling": after.Paths[0].Target.Scaling = 2; break;
                    case "pixelFormat": after.Modes[0].Data.Source.PixelFormat = 3; break;
                    case "scanline": after.Paths[0].Target.ScanLineOrdering = 2; break;
                    case "technology": after.Paths[0].Target.OutputTechnology++; break;
                    // Offset 40 is outside the 20-byte SourceMode but inside its saved 48-byte union.
                    case "sourceUnionTail": after.Modes[0].Data.Target.VideoStandard = 1; break;
                }
                var output = new StringWriter();
                Check(SnapshotComparison.CompareJson(Json(before), Json(after), output) == 1);
                Check(output.ToString().Contains("Desktop/native contract: DIFFERENT"));
            });
        test("Offline snapshot comparison follows mode indices after array reordering", () =>
        {
            var before = Fixture(); var after = Fixture();
            after = after with { Modes = after.Modes.Reverse().ToArray(), Paths = after.Paths.Reverse().ToArray() };
            for (var i = 0; i < after.Paths.Length; i++)
            {
                if (after.Paths[i].Source.ModeIndex != CcdLogic.InvalidIndex)
                    after.Paths[i].Source.ModeIndex = (uint)after.Modes.Length - 1 - after.Paths[i].Source.ModeIndex;
                if (after.Paths[i].Target.ModeIndex != CcdLogic.InvalidIndex)
                    after.Paths[i].Target.ModeIndex = (uint)after.Modes.Length - 1 - after.Paths[i].Target.ModeIndex;
            }
            Check(SnapshotComparison.CompareJson(Json(before), Json(after), new StringWriter()) == 0);
        });
        test("Offline snapshot CLI reads files without changing any input bytes", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "PingXu-snapshot-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var before = Path.Combine(directory, "before.json"); var after = Path.Combine(directory, "after.json");
            try
            {
                File.WriteAllText(before, Json(Fixture()));
                File.WriteAllText(after, Json(Fixture()));
                var a = File.ReadAllBytes(before); var b = File.ReadAllBytes(after);
                Check(SnapshotComparison.Run([before, after], new StringWriter()) == 0);
                Check(a.SequenceEqual(File.ReadAllBytes(before)) && b.SequenceEqual(File.ReadAllBytes(after)));
                File.WriteAllText(after, "{broken");
                Check(SnapshotComparison.Run([before, after], new StringWriter()) == 2);
            }
            finally
            {
                File.Delete(before); File.Delete(after); Directory.Delete(directory);
            }
        });
        test("Offline snapshot CLI rejects missing arguments or files", () =>
        {
            Check(SnapshotComparison.Run([], new StringWriter()) == 2);
            var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.json");
            Check(SnapshotComparison.Run([missing, missing], new StringWriter()) == 2);
        });
    }
    private static string Json(CcdState state) => JsonSerializer.Serialize(new { NativeData = SnapshotCodec.Encode(state) });
}
