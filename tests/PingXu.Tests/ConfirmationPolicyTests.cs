using PingXu.Core;

internal static class ConfirmationPolicyTests
{
    internal static void Register(Action<string, Action> test)
    {
        test("Smart remembers only an explicitly confirmed saved request in the same environment", () =>
        {
            var p = Profile(); var hardware = Hardware(); var trust = new List<string>();
            var first = ConfirmationPolicy.Evaluate(new(), p, [p], hardware, "machine/driver-v1", trust);
            Check(first.RequiresConfirmation && first.CanRemember && first.TimeoutSeconds == 20);
            Check(!string.IsNullOrWhiteSpace(first.TrustKey) && trust.Count == 0);
            trust.Add(first.TrustKey!); // The caller records trust only after explicit successful confirmation.
            var again = ConfirmationPolicy.Evaluate(new(), p, [p], hardware, "machine/driver-v1", trust);
            Check(!again.RequiresConfirmation && !again.CanRemember && again.TrustKey == first.TrustKey);
            Check(trust.Count == 1);
        });
        foreach (var mode in new[] { ConfirmationMode.Smart, ConfirmationMode.Always, ConfirmationMode.Never })
            foreach (var seconds in new[] { 20, 30, 60 })
                test($"Confirmation options accept {mode}/{seconds} with no automatic trust writes", () =>
                {
                    var options = new ConfirmationOptions(mode, seconds); options.Validate();
                    var p = Profile(); var trust = new List<string>();
                    var result = ConfirmationPolicy.Evaluate(options, p, [p], Hardware(), "environment", trust);
                    Check(result.TimeoutSeconds == seconds && result.RequiresConfirmation == (mode != ConfirmationMode.Never));
                    Check(trust.Count == 0);
                    if (mode != ConfirmationMode.Smart) Check(result.TrustKey is null && !result.CanRemember);
                });
        foreach (var seconds in new[] { int.MinValue, -1, 0, 19, 21, 29, 31, 59, 61, int.MaxValue })
            test($"Confirmation options reject timeout {seconds} even in Never mode", () =>
                Throws(() => ConfirmationPolicy.Evaluate(new(ConfirmationMode.Never, seconds), Profile(), [], Hardware(), null, [])));
        foreach (var mode in new[] { (ConfirmationMode)(-1), (ConfirmationMode)3, (ConfirmationMode)999 })
            test($"Unknown confirmation enum {(int)mode} cannot mean Never", () =>
            {
                Throws(() => new ConfirmationOptions(mode).Validate());
                Throws(() => ConfirmationPolicy.Evaluate(new(mode), Profile(), [], Hardware(), null, []));
            });
        foreach (var environment in new string?[] { null, "", "   ", "bad\nenvironment", "\uD800" })
            test($"Missing or malformed environment cannot be remembered ({environment?.Length.ToString() ?? "null"})", () =>
            {
                var p = Profile(); var r = ConfirmationPolicy.Evaluate(new(), p, [p], Hardware(), environment, [Key(p)]);
                Closed(r);
            });
        test("Changing environment requires new explicit trust, including case changes", () =>
        {
            var p = Profile(); var old = Key(p);
            foreach (var environment in new[] { "driver-v2", "ENVIRONMENT", "environment " })
            {
                var r = ConfirmationPolicy.Evaluate(new(), p, [p], Hardware(), environment, [old]);
                Check(r.RequiresConfirmation && r.CanRemember && r.TrustKey != old);
            }
        });
        test("A newly connected screen invalidates trust even when currently disabled", () =>
        {
            var p = Profile(); var hardware = Hardware();
            hardware.Add(hardware[1] with { Id = "new-screen", Enabled = false, Primary = false });
            var r = ConfirmationPolicy.Evaluate(new(), p, [p], hardware, "environment", [Key(p)]);
            Check(r.RequiresConfirmation && r.CanRemember && r.TrustKey != Key(p));
        });
        test("Missing requested enabled screen is not trusted or automatically downgraded", () =>
        {
            var p = Profile(); var hardware = Hardware(); hardware[1] = hardware[1] with { Connected = false };
            Closed(ConfirmationPolicy.Evaluate(new(), p, [p], hardware, "environment", [Key(p)]));
            Check(p.Displays[1].Enabled);
        });
        foreach (var variant in new[] { "duplicate", "empty-id", "no-connected", "null-item" })
            test($"Invalid connected identity set fails closed: {variant}", () =>
            {
                var p = Profile(); var hardware = Hardware();
                switch (variant)
                {
                    case "duplicate": hardware.Add(hardware[0] with { Id = "SCREEN-a" }); break;
                    case "empty-id": hardware[1] = hardware[1] with { Id = " " }; break;
                    case "no-connected": hardware = hardware.Select(d => d with { Connected = false }).ToList(); break;
                    case "null-item": hardware.Add(null!); break;
                }
                Closed(ConfirmationPolicy.Evaluate(new(), p, [p], hardware, "environment", [Key(p)]));
            });
        test("Round trips, channel renumbering, aliases, mode lists and disconnected additions retain trust", () =>
        {
            var p = Profile(); var hardware = Hardware().Select(d => d with
            {
                DeviceName = "DISPLAY99", Name = "改名", Enabled = false, Primary = false,
                X = -9876, Y = 42, Width = 2160, Height = 3840, Rotation = 90, RefreshRate = 120,
                Modes = [new(640, 480, 60)]
            }).ToList();
            hardware.Add(hardware[0] with { Id = "offline", Connected = false });
            var renamed = p with { Name = "改名不编辑布局" };
            var r = ConfirmationPolicy.Evaluate(new(), renamed, [p], hardware, "environment", [Key(p)]);
            Check(!r.RequiresConfirmation && r.TrustKey == Key(p) && !r.CanRemember);
        });
        test("Canonical identity and ordering are culture independent", () =>
        {
            var p = Profile(); var old = Key(p); var culture = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new("tr-TR");
                var reversed = p with { Displays = p.Displays.Select(t => t with { Id = t.Id.ToUpperInvariant() }).Reverse().ToList() };
                var hardware = Hardware().Select(d => d with { Id = d.Id.ToUpperInvariant() }).Reverse().ToList();
                var r = ConfirmationPolicy.Evaluate(new(TimeoutSeconds: 60), reversed,
                    [p with { Id = "other" }, p], hardware, "environment", [old]);
                Check(!r.RequiresConfirmation && r.TrustKey == old && r.TimeoutSeconds == 60);
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
        });
        test("Custom, different ID, and case-distinct profile IDs cannot borrow saved trust", () =>
        {
            var p = Profile(); var key = Key(p);
            Closed(ConfirmationPolicy.Evaluate(new(), p, [], Hardware(), "environment", [key]));
            foreach (var id in new[] { "draft", "PRESET-ONE" })
            {
                var other = p with { Id = id };
                Closed(ConfirmationPolicy.Evaluate(new(), other, [p], Hardware(), "environment", [key]));
                var r = ConfirmationPolicy.Evaluate(new(), other, [other], Hardware(), "environment", [key]);
                Check(r.RequiresConfirmation && r.CanRemember && r.TrustKey != key);
            }
        });
        foreach (var field in new[] { "id", "enabled", "primary", "x", "y", "width", "height", "rotation", "hz" })
            test($"Edited target {field} cannot reuse or remember the saved version", () =>
            {
                var p = Profile(); var edited = p with { Displays = p.Displays.ToList() }; var t = edited.Displays[1];
                edited.Displays[1] = field switch
                {
                    "id" => t with { Id = "other-screen" }, "enabled" => t with { Enabled = false },
                    "primary" => t with { Primary = true }, "x" => t with { X = 4000 }, "y" => t with { Y = 10 },
                    "width" => t with { Width = 1600 }, "height" => t with { Height = 900 },
                    "rotation" => t with { Rotation = 180 }, _ => t with { RefreshRate = 75 }
                };
                Closed(ConfirmationPolicy.Evaluate(new(), edited, [p], Hardware(), "environment", [Key(p)]));
            });
        test("Saving a valid edit produces a different key that needs explicit confirmation", () =>
        {
            var p = Profile(); var edited = p with { Displays = p.Displays.ToList() };
            edited.Displays[1] = edited.Displays[1] with { Rotation = 180 };
            var r = ConfirmationPolicy.Evaluate(new(), edited, [edited], Hardware(), "environment", [Key(p)]);
            Check(r.RequiresConfirmation && r.CanRemember && r.TrustKey != Key(p));
        });
        test("Disabled absent targets are allowed but their saved content is conservatively bound", () =>
        {
            var p = Profile(); p.Displays.Add(new("offline", false, false, 0, 0, 0, 0, 0, 0));
            var key = Key(p); var edited = p with { Displays = p.Displays.ToList() };
            edited.Displays[2] = edited.Displays[2] with { X = 20 };
            Closed(ConfirmationPolicy.Evaluate(new(), edited, [p], Hardware(), "environment", [key]));
            Check(!ConfirmationPolicy.Evaluate(new(), p, [p], Hardware(), "environment", [key]).RequiresConfirmation);
        });
        test("Duplicate saved IDs and malformed target collections fail closed", () =>
        {
            var p = Profile(); Closed(ConfirmationPolicy.Evaluate(new(), p, [p, p], Hardware(), "environment", []));
            foreach (var targets in new List<DisplayTarget>?[] { null, [], [null!], [p.Displays[0], p.Displays[0]],
                [p.Displays[0], new("offline", false, true, 0, 0, 0, 0, 0, 0)] })
            {
                var bad = p with { Displays = targets! };
                Closed(ConfirmationPolicy.Evaluate(new(), bad, [bad], Hardware(), "environment", []));
            }
        });
        test("Always ignores trusted keys and Never with missing context cannot create trust", () =>
        {
            var p = Profile();
            Closed(ConfirmationPolicy.Evaluate(new(ConfirmationMode.Always), p, [p], Hardware(), "environment", [Key(p)]));
            var r = ConfirmationPolicy.Evaluate(new(ConfirmationMode.Never), p, [], [], null, []);
            Check(!r.RequiresConfirmation && !r.CanRemember && r.TrustKey is null);
        });
        test("Hash keys are opaque exact tokens and evaluating never mutates caller inputs", () =>
        {
            var p = Profile(); var hardware = Hardware(); var saved = new List<DisplayProfile> { p };
            var key = Key(p); var trust = new List<string> { key.ToLowerInvariant(), "not-a-key" };
            var before = System.Text.Json.JsonSerializer.Serialize(new { p, hardware, saved, trust });
            var r = ConfirmationPolicy.Evaluate(new(), p, saved, hardware, "environment", trust);
            Check(r.RequiresConfirmation && r.CanRemember && r.TrustKey == key && key.StartsWith("v1:") && key.Length == 67);
            Check(before == System.Text.Json.JsonSerializer.Serialize(new { p, hardware, saved, trust }));
        });
    }

    private static DisplayProfile Profile() => new("preset-one", "横屏", [
        new("screen-A", true, true, 0, 0, 3840, 2160, 0, 60),
        new("screen-B", true, false, 3840, 0, 1920, 1080, 0, 60)]);
    private static List<DisplayInfo> Hardware() => [
        new("screen-A", "DISPLAY1", "4K", true, true, true, 0, 0, 3840, 2160, 0, 60, []),
        new("screen-B", "DISPLAY2", "副屏", true, true, false, 3840, 0, 1920, 1080, 0, 60, [])];
    private static void Check(bool value) { if (!value) throw new Exception("confirmation policy assertion failed"); }
    private static string Key(DisplayProfile p) => ConfirmationPolicy.Evaluate(new(), p, [p], Hardware(), "environment", []).TrustKey
        ?? throw new Exception("valid fixture must be eligible for explicit confirmation");
    private static void Closed(ConfirmationDecision r) => Check(r.RequiresConfirmation && !r.CanRemember && r.TrustKey is null);
    private static void Throws(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception("expected argument validation error");
    }
}
