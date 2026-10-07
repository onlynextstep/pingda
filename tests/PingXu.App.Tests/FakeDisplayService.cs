using PingXu.Core;

namespace PingXu.App.Tests;

// No native backend is constructed, including for Capture/Validate.
internal sealed class FakeDisplayService : IDisplayService
{
    public const string Left = "FAKE-4K-LEFT";
    public const string Middle = "FAKE-UW-MIDDLE";
    public const string Right = "FAKE-UW-RIGHT";
    public List<string> Calls { get; } = [];

    // Refresh rates are fixture assumptions, not measurements of the user's hardware.
    public static DesktopSnapshot Snapshot() => new([
        new(Left, "FAKE_DISPLAY_A", "模拟 4K", true, true, false,
            -7280, 0, 3840, 2160, 0, 60, [new(3840, 2160, 60), new(1920, 1080, 60)]),
        new(Middle, "FAKE_DISPLAY_B", "模拟同型号带鱼屏", true, true, false,
            -3440, 0, 3440, 1440, 0, 60, [new(3440, 1440, 60)]),
        new(Right, "FAKE_DISPLAY_C", "模拟同型号带鱼屏", true, true, true,
            0, 0, 3440, 1440, 0, 60, [new(3440, 1440, 60)])
    ], "FAKE-HARDWARE-NO-NATIVE-SNAPSHOT");

    private T Unexpected<T>(string operation)
    {
        Calls.Add(operation);
        throw new InvalidOperationException($"离屏 UI 测试不应调用显示服务：{operation}");
    }

    public DesktopSnapshot Capture() => Unexpected<DesktopSnapshot>(nameof(Capture));
    public OperationResult Validate(DisplayProfile profile) => Unexpected<OperationResult>(nameof(Validate));
    public OperationResult Apply(DisplayProfile profile, bool persist) => Unexpected<OperationResult>(nameof(Apply));
    public OperationResult Restore(DesktopSnapshot snapshot) => Unexpected<OperationResult>(nameof(Restore));
}
