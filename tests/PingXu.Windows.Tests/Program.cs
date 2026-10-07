using System.Reflection;
using System.Runtime.InteropServices;

// Default execution contains no real native backend and cannot change displays.
var failed = 0;
void Test(string name, Action test)
{
    try { test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.GetBaseException().Message}"); }
}
void Equal(object expected, object? actual)
{
    if (!Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}");
}
var assembly = Assembly.Load("PingXu.Windows");
if (args.SequenceEqual(new[] { "--readback-tests" }))
{
    ReadbackStabilityTests.Run(Test);
    Console.WriteLine($"Failed: {failed}");
    return failed == 0 ? 0 : 1;
}
if (args.Length > 0 && args[0] == "--compare-snapshots")
    return SnapshotComparison.Run(args.Skip(1).ToArray(), Console.Out);
if (args.SequenceEqual(new[] { "--rotation-compatibility-live" })) return RotationCompatibilityProbe.Run();
if (args.SequenceEqual(new[] { "--diagnose-live" })) return LiveValidation.Diagnose();
if (args.SequenceEqual(new[] { "--validate-live" }))
    return LiveValidation.Validate(new PingXu.Windows.Win32CcdApi(), Console.Out);
if (args.Length == 2 && args[0] == "--validate-profiles-live")
    return LiveValidation.ValidateProfiles(args[1]);
if (args.Length == 3 && args[0] == "--reenable-parameters-live")
    return ReenableParameterProbe.Run(args[1], args[2]);
if (args.SequenceEqual(new[] { "--capture-live" }))
{
    // Explicit opt-in READ ONLY integration probe. No Validate, Apply or Restore call exists here.
    try
    {
        var type = assembly.GetType("PingXu.Windows.WindowsDisplayService")!;
        var service = (PingXu.Core.IDisplayService)Activator.CreateInstance(type)!;
        var snapshot = service.Capture();
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(snapshot, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    catch (Exception e) { Console.WriteLine(e.GetBaseException().Message); return 1; }
}
if (args.Length != 0) { Console.Error.WriteLine("Unknown test arguments: " + string.Join(" ", args)); return 2; }
ReenableTests.Run(Test);
Type NativeType(string name) => assembly.GetType($"PingXu.Windows.{name}")
    ?? throw new Exception($"Native ABI type {name} is missing");
// Literal sizes and offsets are from Windows SDK 10.0.26100.0 wingdi.h (x64 ABI).
foreach (var (name, size) in new[] { ("Luid", 8), ("Rational", 8), ("SourceMode", 20),
    ("VideoSignal", 48), ("ModeInfo", 64), ("PathSource", 20), ("PathTarget", 48),
    ("PathInfo", 72), ("DeviceInfoHeader", 20), ("TargetDeviceName", 420),
    ("SourceDeviceName", 84), ("TargetPreferredMode", 80), ("DevMode", 220) })
    Test($"ABI {name} size {size}", () => Equal(size, Marshal.SizeOf(NativeType(name))));
foreach (var (name, field, offset) in new[] { ("ModeInfo", "Data", 16),
    ("PathInfo", "Target", 20), ("PathInfo", "Flags", 68),
    ("PathTarget", "Available", 40), ("TargetDeviceName", "MonitorDevicePath", 164),
    ("DevMode", "Size", 68), ("DevMode", "Width", 172), ("DevMode", "Frequency", 184) })
    Test($"ABI {name}.{field} offset", () => Equal(offset, Marshal.OffsetOf(NativeType(name), field).ToInt32()));
foreach (var (rotation, width, height) in new[] { (0, 3840, 2160), (90, 2160, 3840),
    (180, 3840, 2160), (270, 2160, 3840) })
    Test($"Native resolution to desktop at {rotation} degrees", () =>
    {
        var method = NativeType("DisplayConversion").GetMethod("DesktopSize", BindingFlags.Static | BindingFlags.Public)!;
        Equal((width, height), method.Invoke(null, [3840, 2160, rotation]));
    });
Test("Invalid rotation is rejected", () =>
{
    var method = NativeType("DisplayConversion").GetMethod("DesktopSize", BindingFlags.Static | BindingFlags.Public)!;
    try { method.Invoke(null, [3840, 2160, 45]); }
    catch (TargetInvocationException e) when (e.InnerException is ArgumentException) { return; }
    throw new Exception("45 degree rotation was accepted");
});
AdapterTests.Run(Test);
ReadbackStabilityTests.Run(Test);
DisplayCapacityRaceTests.Run(Test);
ExplicitRefreshTests.Run(Test);
PlanningRegressionTests.Run(Test);
SnapshotComparisonTests.Run(Test);
Test("Native service has parameterless public constructor", () =>
{
    if (assembly.GetType("PingXu.Windows.WindowsDisplayService")!.GetConstructor(Type.EmptyTypes) is null)
        throw new Exception("Default native constructor is missing");
});
Test("DEVMODE portrait dimensions normalize to native resolution", () =>
{
    var method = NativeType("DisplayConversion").GetMethod("FromDevMode", BindingFlags.Static | BindingFlags.NonPublic);
    if (method is null) throw new Exception("DEVMODE conversion is missing");
    var dm = new PingXu.Windows.DevMode { Width = 2160, Height = 3840, Orientation = 1, Fields = 0x80, Frequency = 60, BitsPerPel = 32 };
    Equal(new PingXu.Core.DisplayMode(3840, 2160, 60), method.Invoke(null, [dm]));
});
Test("DEVMODE interlaced or default-frequency modes are not advertised", () =>
{
    var method = NativeType("DisplayConversion").GetMethod("FromDevMode", BindingFlags.Static | BindingFlags.NonPublic);
    if (method is null) throw new Exception("DEVMODE conversion is missing");
    foreach (var dm in new[] {
        new PingXu.Windows.DevMode { Width = 1920, Height = 1080, Frequency = 60, DisplayFlags = 2, BitsPerPel = 32 },
        new PingXu.Windows.DevMode { Width = 1920, Height = 1080, Frequency = 1, BitsPerPel = 32 } })
        if (method.Invoke(null, [dm]) is not null) throw new Exception("Unrepresentable mode was advertised");
});
Console.WriteLine($"Failed: {failed}");
return failed == 0 ? 0 : 1;
