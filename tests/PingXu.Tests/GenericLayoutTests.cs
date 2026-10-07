using PingXu.Core;

internal static class GenericLayoutTests
{
    internal static void Register(Action<string, Action> test)
    {
        test("Generic arrange never enables a connected inactive display implicitly", () =>
        {
            var hardware = Desk(2);
            hardware.Add(new("inactive", "DISPLAY3", "同型号", true, false, false, 0, 0, 0, 0, 0, 0, []));
            var result = LayoutPlanner.Arrange(hardware, "panel-0", 90, false, false);
            Check(result.Count(d => d.Enabled) == 2 && !result.Single(d => d.Id == "inactive").Enabled);
        });
        test("Generic arrange resolves monitor identity case insensitively", () =>
        {
            var result = LayoutPlanner.Arrange(Desk(2), "PANEL-0", 90, false, false);
            Check(result.Single(d => d.Id == "panel-0").Rotation == 90);
        });
        test("Generic arrange rejects an unknown selected screen instead of a successful no-op", () =>
            Throws(() => LayoutPlanner.Arrange(Desk(2), "absent", 90, false, false)));
        foreach (var angle in new[] { 0, 90, 180, 270 })
            test($"Generic arrange converts already-portrait desktop dimensions to {angle}", () =>
            {
                var hardware = Desk(2); hardware[0] = hardware[0] with { Width = 1600, Height = 2560, Rotation = 90 };
                var result = LayoutPlanner.ArrangeHorizontal(hardware, "panel-0", angle);
                var target = result.Single(d => d.Id == "panel-0");
                Check(target.Width == (angle % 180 == 90 ? 1600 : 2560) && target.Height == (angle % 180 == 90 ? 2560 : 1600));
                Check(target.RefreshRate == 75);
            });
        test("Generic arrange cannot invent an inactive selected screen's operating mode", () =>
        {
            var hardware = Desk(2); hardware[1] = hardware[1] with { Enabled = false };
            Throws(() => LayoutPlanner.ArrangeHorizontal(hardware, "panel-1", 90, onlySelected: true));
        });
        test("Generic arrange rejects conflicting switches and disabling the last active screen", () =>
        {
            Throws(() => LayoutPlanner.ArrangeHorizontal(Desk(2), "panel-0", 0, true, true));
            Throws(() => LayoutPlanner.ArrangeHorizontal(Desk(1), "panel-0", 0, disableSelected: true));
        });
        test("Generic arrange preserves inactive positions and never mutates the captured list", () =>
        {
            var hardware = Desk(2);
            hardware.Add(new("offline", "", "legacy", false, false, false, -5000, 44, 1920, 1080, 180, 60, []));
            var before = System.Text.Json.JsonSerializer.Serialize(hardware);
            var result = LayoutPlanner.ArrangeHorizontal(hardware, "panel-0", 180);
            Check(result.Single(d => d.Id == "offline") is { Enabled: false, Primary: false, X: -5000, Y: 44, Rotation: 180 });
            Check(before == System.Text.Json.JsonSerializer.Serialize(hardware));
        });
        test("Core validation rejects null and blank identity instead of accidental matching", () =>
        {
            Throws(() => LayoutPlanner.Check(new("p", "test", [null!]), Desk(1)));
            Throws(() => LayoutPlanner.Check(new("p", "test", [new(" ", true, true, 0, 0, 1920, 1080, 0, 60)]),
                [Desk(1)[0] with { Id = " " }]));
        });
    }

    internal static List<DisplayInfo> Desk(int count) => Enumerable.Range(0, count).Select(i =>
        new DisplayInfo($"panel-{i}", $"DISPLAY{i + 1}", "同型号", true, true, i == 0,
            i == 0 ? 0 : 2560 + (i - 1) * 1920, i * 17, i == 0 ? 2560 : 1920, i == 0 ? 1600 : 1200,
            0, i == 0 ? 75 : 100, [])).ToList();
    internal static void Check(bool value) { if (!value) throw new Exception("generic layout assertion failed"); }
    internal static void Throws(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception("expected invalid layout rejection");
    }
}
