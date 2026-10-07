using PingXu.Core;
using PingXu.Windows;
using static AdapterTests;

internal static class RawSourceContractTests
{
    // Evidence: the user's protected 90-degree trial read back CCD source=3840x2160,
    // path rotation=ROTATE90, rate=60/1. Identity/LUID below are normalized test values.
    // MartinGC94/DisplayConfig 7de36ba3 API/DisplayConfig.cs:265-270 changes only path.rotation.
    // Expected raw dimensions are literal observations, never computed from Build's output.
    internal static void Run(Action<string, Action> test)
    {
        foreach (var (angle, width, height) in new[] { (0, 3840, 2160), (90, 2160, 3840), (180, 3840, 2160), (270, 2160, 3840) })
            test($"Observed unrotated CCD source exposes Core desktop {width}x{height} at {angle}", () =>
            {
                var state = ObservedRaw(angle); var api = new FakeApi { State = state };
                var capture = new WindowsDisplayService(api).Capture();
                var display = capture.Displays.Single();
                Check(display.Width == width && display.Height == height && display.Rotation == angle);
                Check(display.X == 0 && display.Y == 0 && display.Primary && display.RefreshRate == 60);
                // Empty enumerated mode list ensures this assertion exercises current-mode supplementation.
                Check(display.Modes.SequenceEqual(new[] { new DisplayMode(3840, 2160, 60) }));
                var decoded = SnapshotCodec.Decode(capture.NativeData);
                Check(SnapshotCodec.Bytes(decoded.Paths).SequenceEqual(SnapshotCodec.Bytes(state.Paths)));
                Check(SnapshotCodec.Bytes(decoded.Modes).SequenceEqual(SnapshotCodec.Bytes(state.Modes)));
            });
        foreach (var angle in new[] { 90, 270 })
            test($"Core portrait {angle} writes observed raw source without swapping its dimensions", () =>
            {
                var before = ObservedRaw(0); var request = Portrait(angle);
                var plan = CcdLogic.Build(before, request); var path = plan.Paths.Single();
                var source = plan.Modes[path.Source.ModeIndex].Data.Source;
                Check(source.Width == 3840 && source.Height == 2160);
                Check(path.Target.Rotation == (angle == 90 ? 2u : 4u));
                Check(SnapshotCodec.Bytes(new[] { source }).SequenceEqual(SnapshotCodec.Bytes(new[] { before.Modes[0].Data.Source })));
                Check(path.Target.ModeIndex != CcdLogic.InvalidIndex);
                Check(SnapshotCodec.Bytes(new[] { plan.Modes[path.Target.ModeIndex].Data.Target })
                    .SequenceEqual(SnapshotCodec.Bytes(new[] { before.Modes[1].Data.Target })));
                Check(request.Displays.Single().Width == 2160 && request.Displays.Single().Height == 3840);
            });
        foreach (var angle in new[] { 0, 90, 270 })
            test($"Observed portrait to {angle} keeps the original native target timing", () =>
            {
                var before = ObservedRaw(90);
                var request = angle == 0 ? Profile(new DisplayTarget("observed-4k", true, true, 0, 0, 3840, 2160, 0, 60)) : Portrait(angle);
                var plan = CcdLogic.Build(before, request); var path = plan.Paths.Single();
                Check(path.Target.ModeIndex != CcdLogic.InvalidIndex);
                Check(plan.Modes[path.Source.ModeIndex].Data.Source.Width == 3840);
                Check(plan.Modes[path.Source.ModeIndex].Data.Source.Height == 2160);
                Check(SnapshotCodec.Bytes(new[] { plan.Modes[path.Target.ModeIndex].Data.Target })
                    .SequenceEqual(SnapshotCodec.Bytes(new[] { before.Modes[1].Data.Target })));
            });
        test("Independent observed portrait readback satisfies the unchanged Core request", () =>
        {
            var api = new ObservedApi();
            var result = new WindowsDisplayService(api).Apply(Portrait(90), false);
            if (!result.Success) throw new Exception(result.Message);
            Check(api.Flags.SequenceEqual(new uint[] { 0x60, 0xA0 }));
        });
        foreach (var persist in new[] { false, true })
            test($"Observed portrait snapshot restore persist={persist} preserves all raw native bytes", () =>
            {
                var saved = ObservedRaw(90); var api = new FakeApi { State = ObservedRaw(0) };
                Check(new WindowsDisplayService(api).Restore(new([], SnapshotCodec.Encode(saved)), persist).Success);
                Check(SnapshotCodec.Bytes(api.State.Paths).SequenceEqual(SnapshotCodec.Bytes(saved.Paths)));
                Check(SnapshotCodec.Bytes(api.State.Modes).SequenceEqual(SnapshotCodec.Bytes(saved.Modes)));
            });
        test("Extended positions use rotated Core desktop width while source width stays unrotated", () =>
        {
            var plan = CcdLogic.Build(Fixture(), Profile(
                new("monitor-A", true, true, 0, 0, 2160, 3840, 90, 60),
                new("monitor-B", true, false, 2160, 0, 1920, 1080, 0, 60)));
            var first = plan.Paths.Single(p => p.Target.Id == 10);
            var second = plan.Paths.Single(p => p.Target.Id == 11);
            Check(plan.Modes[first.Source.ModeIndex].Data.Source.Width == 3840);
            Check(plan.Modes[second.Source.ModeIndex].Data.Source.Position.X == 2160);
        });
    }

    private static DisplayProfile Portrait(int angle) => Profile(new DisplayTarget("observed-4k", true, true, 0, 0, 2160, 3840, angle, 60));
    private static CcdState ObservedRaw(int angle)
    {
        var adapter = new Luid { Low = 7 };
        var signal = new VideoSignal { PixelRate = 594000000, HSync = new() { Numerator = 135000, Denominator = 1 },
            VSync = new() { Numerator = 60, Denominator = 1 }, ActiveSize = new() { Width = 3840, Height = 2160 },
            TotalSize = new() { Width = 4400, Height = 2250 }, ScanLineOrdering = 1 };
        return new(
            [new() { Flags = 1, Source = new() { AdapterId = adapter, Id = 0, ModeIndex = 0, StatusFlags = 1 },
                Target = new() { AdapterId = adapter, Id = 4356, ModeIndex = 1, Available = 1, StatusFlags = 1,
                    Rotation = (uint)(angle / 90 + 1), Scaling = 1, RefreshRate = signal.VSync, ScanLineOrdering = 1, OutputTechnology = 5 } }],
            [new() { Type = 1, Id = 0, AdapterId = adapter, Data = new() { Source = new() { Width = 3840, Height = 2160, PixelFormat = 4 } } },
                new() { Type = 2, Id = 4356, AdapterId = adapter, Data = new() { Target = signal } }],
            [new(new(adapter, 4356), "observed-4k", "4K", @"\\.\DISPLAY1", [], signal)]);
    }
    private sealed class ObservedApi : ICcdApi
    {
        private bool applied;
        internal readonly List<uint> Flags = [];
        public CcdState Read() => ObservedRaw(applied ? 90 : 0);
        public int Set(CcdPlan plan, uint flags)
        {
            Flags.Add(flags);
            var source = plan.Modes[plan.Paths.Single().Source.ModeIndex].Data.Source;
            if (source.Width != 3840 || source.Height != 2160) return 31;
            if (flags == 0xA0) applied = true;
            else Check(flags == 0x60);
            return 0;
        }
    }
}
