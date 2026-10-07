using System.Text.Json;
using PingXu.Core;

internal static class DisplayLimitTests
{
    internal static void Register(Action<string, Action> test)
    {
        // A guard based on enabled targets (or enabled hardware) misses these connected devices.
        foreach (var activeCount in new[] { 1, 7 })
        {
            test($"Seven connected devices with {activeCount} active reject a one-screen preset", () =>
            {
                var hardware = SevenConnected(activeCount);
                var profile = new DisplayProfile("saved", "旧单屏预设", [Target(hardware[0])]);
                var before = JsonSerializer.Serialize(profile);
                RejectLimit(() => LayoutPlanner.Check(profile, hardware));
                Equal(JsonSerializer.Serialize(profile), before);
            });
            test($"Seven connected devices with {activeCount} active cannot generate truncated defaults", () =>
            {
                var hardware = SevenConnected(activeCount);
                var before = JsonSerializer.Serialize(hardware);
                RejectLimit(() => DefaultProfileFactory.Create(hardware));
                Equal(JsonSerializer.Serialize(hardware), before);
            });
        }
        test("Seven connected devices cannot bypass arrange limit through onlySelected", () =>
            RejectLimit(() => LayoutPlanner.ArrangeHorizontal(SevenConnected(1), "panel-0", 90, onlySelected: true)));

        // Literal mixed desktop footprints, rotations and Hz catch dropped identities and invented modes.
        for (var count = 1; count <= 6; count++)
        {
            var screenCount = count;
            test($"Arrange and disk roundtrip retain all fields for {screenCount} mixed screens", () =>
            {
                var hardware = MixedDesk().Take(screenCount).Reverse().ToList();
                var before = JsonSerializer.Serialize(hardware);
                var arranged = LayoutPlanner.Arrange(hardware, "PANEL-0", 90, false, false);
                DisplayTarget[] expected = [
                    new("panel-0", true, true, 0, 0, 1080, 1920, 90, 59),
                    new("panel-1", true, false, 1080, 0, 1440, 2560, 90, 75),
                    new("panel-2", true, false, 2520, 0, 3840, 2160, 0, 120),
                    new("panel-3", true, false, 6360, 0, 1200, 1920, 270, 60),
                    new("panel-4", true, false, 7560, 0, 3440, 1440, 180, 144),
                    new("panel-5", true, false, 11000, 0, 2560, 1600, 0, 165)];
                Require(arranged.SequenceEqual(expected.Take(screenCount)), "arranged identity/mode/position mismatch");
                var profile = new DisplayProfile("user-id", "混合屏幕", arranged);
                LayoutPlanner.Check(profile, hardware);
                Roundtrip(profile);
                Equal(JsonSerializer.Serialize(hardware), before);
            });
            test($"Defaults retain {screenCount} mixed screens and all single-screen candidates", () =>
            {
                var hardware = MixedDesk().Take(screenCount).ToList();
                var defaults = DefaultProfileFactory.Create(hardware);
                Equal(defaults.Count, screenCount == 1 ? 1 : screenCount + 1);
                Require(defaults[0].Displays.SequenceEqual(hardware.Select(Target)), "defaults changed observations");
                foreach (var profile in defaults)
                {
                    LayoutPlanner.Check(profile, hardware);
                    Roundtrip(profile);
                }
                foreach (var profile in defaults.Skip(1))
                {
                    var single = profile.Displays.Single(d => d.Enabled);
                    var original = hardware.Single(d => d.Id == single.Id);
                    Equal(single, Target(original) with { Primary = true, X = 0, Y = 0 });
                    Equal(profile.Displays.Count, screenCount);
                }
                if (screenCount > 1)
                    Equal(defaults.Skip(1).Select(p => p.Displays.Single(d => d.Enabled).Id).Distinct().Count(), screenCount);
            });
        }
        test("Six connected devices with five inactive are allowed without implicitly enabling them", () =>
        {
            var hardware = SevenConnected(1).Take(6).ToList();
            var profiles = DefaultProfileFactory.Create(hardware);
            Equal(profiles.Count, 1);
            Require(profiles[0].Displays.SequenceEqual(hardware.Select(Target)), "inactive devices changed");
            var arranged = LayoutPlanner.ArrangeHorizontal(hardware, "panel-0", 90);
            Equal(arranged.Count, 6);
            Equal(arranged.Count(t => t.Enabled), 1);
            LayoutPlanner.Check(new("one", "单屏", [Target(hardware[0])]), hardware);
        });
        test("Six connected plus 26 offline records remain usable and survive saving", () =>
        {
            var hardware = MixedDesk();
            hardware.AddRange(Enumerable.Range(0, 26).Select(i => new DisplayInfo($"offline-{i}", "", "历史设备",
                false, false, false, -9000 - i, 37, 1080, 1920, 270, 119, [])));
            var baseline = DefaultProfileFactory.Create(hardware)[0];
            Equal(baseline.Displays.Count, 32);
            Require(baseline.Displays.SequenceEqual(hardware.Select(Target)), "offline records lost");
            LayoutPlanner.Check(baseline, hardware);
            var arranged = LayoutPlanner.ArrangeHorizontal(hardware, "panel-0", 90);
            Equal(arranged.Count, 32);
            Require(arranged.Where(t => !t.Enabled).OrderBy(t => t.Id)
                .SequenceEqual(baseline.Displays.Where(t => !t.Enabled).OrderBy(t => t.Id)), "offline fields changed");
            Roundtrip(baseline);
            Roundtrip(new("arranged", "带历史设备的排列", arranged));
        });
        // Saved targets do not establish live connection state, including old >6-active layouts.
        test("Legacy 32-record active preset still loads and saves without truncation", () =>
        {
            var profile = new DisplayProfile("legacy-32", "旧桌面", GenericLayoutTests.Desk(32).Select(Target).ToList());
            Roundtrip(profile);
        });
        test("History compatibility still rejects 33 target records", () =>
            GenericLayoutTests.Throws(() => ProfileStore.Parse(JsonSerializer.Serialize(new[] {
                new DisplayProfile("too-many", "超出历史格式", GenericLayoutTests.Desk(33).Select(Target).ToList()) }))));
        test("Over-limit default generation leaves an existing preset file byte-for-byte intact", () =>
            InTemporaryStore((store, directory) =>
            {
                var profile = new DisplayProfile("keep-id", "用户原预设", [Target(MixedDesk()[0])]);
                store.Save([profile]);
                var path = Path.Combine(directory, "profiles.json");
                var before = File.ReadAllBytes(path);
                RejectLimit(() => store.Save(DefaultProfileFactory.Create(SevenConnected(1))));
                Require(File.ReadAllBytes(path).SequenceEqual(before), "existing preset bytes changed");
                Equal(store.Load().Single().Id, "keep-id");
                Equal(Directory.GetFiles(directory).Length, 1);
            }));
    }

    private static List<DisplayInfo> MixedDesk() => [
        new("panel-0", "DISPLAY6", "同型号", true, true, true, 0, 0, 1920, 1080, 0, 59, [new(1920, 1080, 59)]),
        new("panel-1", "DISPLAY2", "同型号", true, true, false, 1920, 13, 1440, 2560, 90, 75, [new(2560, 1440, 75)]),
        new("panel-2", "DISPLAY5", "同型号", true, true, false, 3360, -20, 3840, 2160, 0, 120, [new(3840, 2160, 120)]),
        new("panel-3", "DISPLAY1", "同型号", true, true, false, 7200, 7, 1200, 1920, 270, 60, [new(1920, 1200, 60)]),
        new("panel-4", "DISPLAY4", "同型号", true, true, false, 8400, -40, 3440, 1440, 180, 144, [new(3440, 1440, 144)]),
        new("panel-5", "DISPLAY3", "同型号", true, true, false, 11840, 25, 2560, 1600, 0, 165, [new(2560, 1600, 165)])];

    private static List<DisplayInfo> SevenConnected(int activeCount) => GenericLayoutTests.Desk(7)
        .Select((d, i) => i < activeCount ? d : d with { Enabled = false, Primary = false,
            Width = 0, Height = 0, RefreshRate = 0 }).ToList();

    private static DisplayTarget Target(DisplayInfo d) => new(d.Id, d.Enabled, d.Primary, d.X, d.Y,
        d.Width, d.Height, d.Rotation, d.RefreshRate);

    private static void RejectLimit(Action action)
    {
        try { action(); }
        catch (ArgumentException error)
        {
            Require(error.Message.Contains('7') && error.Message.Contains('6') && error.Message.Contains("连接"),
                "limit error must explain connected count 7 and supported maximum 6: " + error.Message);
            return;
        }
        throw new Exception("expected connected-screen limit rejection (7 connected, maximum 6)");
    }

    private static void Roundtrip(DisplayProfile profile) => InTemporaryStore((store, _) =>
    {
        store.Save([profile]);
        var loaded = store.Load().Single();
        Equal(loaded.Id, profile.Id);
        Equal(loaded.Name, profile.Name);
        Require(loaded.Displays.SequenceEqual(profile.Displays), "saved target fields changed or lost");
    });

    private static void InTemporaryStore(Action<ProfileStore, string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "PingXu-Limits-" + Guid.NewGuid().ToString("N"));
        try { action(new ProfileStore(directory), directory); }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void Equal<T>(T actual, T expected) => Require(EqualityComparer<T>.Default.Equals(actual, expected),
        $"expected {expected}, got {actual}");
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
