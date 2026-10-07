using PingXu.Core;
using static GenericLayoutTests;

internal static class DefaultProfileFactoryTests
{
    internal static void Register(Action<string, Action> test)
    {
        foreach (var count in new[] { 1, 2, 3, 4 })
            test($"First-run defaults retain exact observed {count}-screen non-4K layout", () =>
            {
                var hardware = Desk(count);
                var defaults = DefaultProfileFactory.Create(hardware);
                Check(defaults.Count == (count == 1 ? 1 : count + 1));
                Check(defaults[0].Displays.SequenceEqual(hardware.Select(Target)));
                Check(defaults.Select(p => p.Id).Distinct().Count() == defaults.Count);
                foreach (var p in defaults) LayoutPlanner.Check(p, hardware);
                foreach (var p in defaults.Skip(1))
                {
                    var single = p.Displays.Single(d => d.Enabled);
                    var measured = hardware.Single(d => d.Id == single.Id);
                    Check(single.Primary && single.X == 0 && single.Y == 0);
                    Check(single.Width == measured.Width && single.Height == measured.Height &&
                        single.RefreshRate == measured.RefreshRate && single.Rotation == measured.Rotation);
                }
            });
        foreach (var angle in new[] { 0, 90, 180, 270 })
            test($"Defaults preserve measured rotation {angle} and nonstandard integer Hz", () =>
            {
                var hardware = Desk(2);
                hardware[1] = hardware[1] with { Width = angle % 180 == 90 ? 1200 : 1920,
                    Height = angle % 180 == 90 ? 1920 : 1200, Rotation = angle, RefreshRate = 59 };
                var result = DefaultProfileFactory.Create(hardware);
                Check(result[0].Displays.SequenceEqual(hardware.Select(Target)));
                var single = result.Skip(1).Select(p => p.Displays.Single(d => d.Enabled)).Single(d => d.Id == "panel-1");
                Check(single.Rotation == angle && single.Width == hardware[1].Width && single.Height == hardware[1].Height && single.RefreshRate == 59);
            });
        test("Default baseline preserves original three-screen gaps and vertical offsets", () =>
        {
            var hardware = new List<DisplayInfo> {
                new("left", "DISPLAY3", "4K", true, true, false, -7280, 0, 3840, 2160, 0, 60, []),
                new("middle", "DISPLAY2", "带鱼", true, true, false, -3440, 4, 3440, 1440, 0, 180, []),
                new("right", "DISPLAY1", "带鱼", true, true, true, 0, 0, 3440, 1440, 0, 180, []) };
            var first = DefaultProfileFactory.Create(hardware)[0];
            Check(first.Displays.SequenceEqual(hardware.Select(Target)));
            Check(first.Displays[0].X == -7280 && first.Displays[1].Y == 4);
        });
        test("Default baseline retains vertical, left and above arrangements without inventing adjacency", () =>
        {
            var hardware = Desk(4);
            hardware[1] = hardware[1] with { X = 200, Y = -1200 };
            hardware[2] = hardware[2] with { X = -1920, Y = -300 };
            hardware[3] = hardware[3] with { X = 0, Y = 1600 };
            var first = DefaultProfileFactory.Create(hardware)[0];
            Check(first.Displays.SequenceEqual(hardware.Select(Target)));
        });
        test("Same-model defaults use stable identities rather than names or DISPLAY numbers", () =>
        {
            var hardware = Desk(4); var result = DefaultProfileFactory.Create(hardware);
            Check(result.Skip(1).Select(p => p.Displays.Single(d => d.Enabled).Id).Distinct().Count() == 4);
            var renumbered = hardware.Select(d => d with { DeviceName = "rebound", Name = "same renamed model" }).Reverse().ToList();
            var other = DefaultProfileFactory.Create(renumbered);
            for (var i = 1; i < result.Count; i++)
                Check(result[i].Displays.OrderBy(d => d.Id).SequenceEqual(other[i].Displays.OrderBy(d => d.Id)));
        });
        test("Defaults retain offline and inactive entries but never advertise guessed modes for them", () =>
        {
            var hardware = Desk(1);
            hardware.Add(new("inactive", "DISPLAY2", "unknown", true, false, false, -9999, 23, 0, 0, 0, 0, [new(1920, 1080, 60)]));
            hardware.Add(new("offline", "", "old", false, false, false, 123, 45, 7680, 4320, 270, 240, []));
            var result = DefaultProfileFactory.Create(hardware);
            Check(result.Count == 1 && result[0].Displays.SequenceEqual(hardware.Select(Target)));
        });
        test("Defaults do not replace active measurements with advertised or preferred-looking modes", () =>
        {
            var hardware = Desk(2);
            hardware[0] = hardware[0] with { Modes = [new(7680, 4320, 240), new(3840, 2160, 60)] };
            var result = DefaultProfileFactory.Create(hardware);
            Check(result.All(p => p.Displays.Single(d => d.Id == "panel-0").Width == 2560));
            Check(result.All(p => p.Displays.Single(d => d.Id == "panel-0").RefreshRate == 75));
        });
        foreach (var invalid in new[] { "empty", "all-off", "clone", "overlap", "duplicate-id", "missing-id", "null-item", "disconnected-active", "inactive-primary", "no-primary", "invalid-angle", "unknown-active-mode" })
            test($"Invalid observations never produce fabricated defaults: {invalid}", () =>
            {
                var hardware = Desk(2);
                switch (invalid)
                {
                    case "empty": hardware.Clear(); break;
                    case "all-off": hardware = hardware.Select(d => d with { Enabled = false, Primary = false }).ToList(); break;
                    case "clone": hardware[1] = hardware[1] with { X = 0, Y = 0 }; break;
                    case "overlap": hardware[1] = hardware[1] with { X = 2000, Y = 100 }; break;
                    case "duplicate-id": hardware[1] = hardware[1] with { Id = "PANEL-0" }; break;
                    case "missing-id": hardware[1] = hardware[1] with { Id = " " }; break;
                    case "null-item": hardware.Add(null!); break;
                    case "disconnected-active": hardware[1] = hardware[1] with { Connected = false }; break;
                    case "inactive-primary": hardware[0] = hardware[0] with { Enabled = false }; break;
                    case "no-primary": hardware[0] = hardware[0] with { Primary = false }; break;
                    case "invalid-angle": hardware[1] = hardware[1] with { Rotation = 45 }; break;
                    case "unknown-active-mode": hardware[1] = hardware[1] with { RefreshRate = 0 }; break;
                }
                GenericLayoutTests.Throws(() => DefaultProfileFactory.Create(hardware));
            });
        test("Default generation does not mutate observations and sibling profile lists are independent", () =>
        {
            var hardware = Desk(4); var before = System.Text.Json.JsonSerializer.Serialize(hardware);
            var result = DefaultProfileFactory.Create(hardware);
            Check(before == System.Text.Json.JsonSerializer.Serialize(hardware));
            result[1].Displays.Clear();
            Check(result[0].Displays.Count == 4 && result[2].Displays.Count == 4 && hardware.Count == 4);
        });
        test("New defaults and legacy offline presets keep the existing JSON schema and user identity", () =>
        {
            const string oldJson = "[{\"Id\":\"my-old-id\",\"Name\":\"我的旧场景\",\"Displays\":[{\"Id\":\"panel-0\",\"Enabled\":true,\"Primary\":true,\"X\":0,\"Y\":0,\"Width\":2560,\"Height\":1600,\"Rotation\":0,\"RefreshRate\":75},{\"Id\":\"old-offline\",\"Enabled\":false,\"Primary\":false,\"X\":-1920,\"Y\":3,\"Width\":1920,\"Height\":1080,\"Rotation\":0,\"RefreshRate\":60}]}]";
            var existing = ProfileStore.Parse(oldJson); var original = existing[0];
            var defaults = DefaultProfileFactory.Create(Desk(4));
            var roundtrip = ProfileStore.Parse(System.Text.Json.JsonSerializer.Serialize(defaults));
            Check(roundtrip.Count == 5 && roundtrip[0].Displays.SequenceEqual(defaults[0].Displays));
            Check(existing.Count == 1 && existing[0] == original && original.Id == "my-old-id" && original.Displays[1].X == -1920);
            LayoutPlanner.Check(original, Desk(1));
        });
        test("Defaults honor a non-first portrait primary without changing any observed desktop fields", () =>
        {
            var hardware = Desk(4);
            var origin = hardware[2];
            hardware = hardware.Select(d => d with { X = d.X - origin.X, Y = d.Y - origin.Y,
                Primary = d.Id == origin.Id }).ToList();
            hardware[2] = hardware[2] with { Width = 1200, Height = 1920, Rotation = 270, RefreshRate = 119 };
            var result = DefaultProfileFactory.Create(hardware);
            Check(result[0].Displays.SequenceEqual(hardware.Select(Target)));
            var firstSingle = result[1].Displays.Single(d => d.Enabled);
            Check(firstSingle == Target(hardware[2]));
            foreach (var profile in result)
            {
                LayoutPlanner.Check(profile, hardware);
                Check(profile.Displays.Count(d => d.Enabled && d.Primary) == 1);
            }
        });
        test("Rejected save preserves an existing user library byte-for-byte", () =>
        {
            // A dedicated temporary directory only; never read or write the actual user's profile store.
            var directory = Path.Combine(Path.GetTempPath(), "PingXu-Core-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new ProfileStore(directory);
                var existing = DefaultProfileFactory.Create(Desk(2));
                existing[0] = existing[0] with { Id = "user-owned-id", Name = "用户自定义名称" };
                store.Save(existing);
                var path = Path.Combine(directory, "profiles.json");
                var before = File.ReadAllBytes(path);
                var invalid = DefaultProfileFactory.Create(Desk(4));
                invalid[0].Displays.Clear();
                GenericLayoutTests.Throws(() => store.Save(invalid));
                Check(before.SequenceEqual(File.ReadAllBytes(path)));
                Check(store.Load()[0].Id == "user-owned-id");
                Check(Directory.GetFiles(directory).Length == 1);
            }
            finally
            {
                // Delete only this test's uniquely named temporary directory.
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        });
    }
    private static DisplayTarget Target(DisplayInfo d) => new(d.Id, d.Enabled, d.Primary, d.X, d.Y,
        d.Width, d.Height, d.Rotation, d.RefreshRate);
}
