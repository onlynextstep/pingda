namespace PingXu.Core;

/// <summary>An enumerated unrotated pixel mode and integer Hz; not a list of universal supported values.</summary>
public record DisplayMode(int Width, int Height, int RefreshRate);
/// <summary>
/// Id is stable device identity (case-insensitive), not Name or the reassignable DISPLAY channel number.
/// Active Width/Height and X/Y are the rotated desktop footprint; Modes use unrotated dimensions.
/// Connected does not imply Enabled. Inactive dimensions/rate are not evidence of an active desktop mode.
/// This model cannot prove native clone/source relationships; the Windows adapter rejects unsupported topology.
/// </summary>
public record DisplayInfo(string Id, string DeviceName, string Name, bool Connected, bool Enabled,
    bool Primary, int X, int Y, int Width, int Height, int Rotation, int RefreshRate,
    IReadOnlyList<DisplayMode> Modes);
/// <summary>Requested rotated desktop dimensions/position. Driver validation is still required before applying.</summary>
public record DisplayTarget(string Id, bool Enabled, bool Primary, int X, int Y,
    int Width, int Height, int Rotation, int RefreshRate);
public record DisplayProfile(string Id, string Name, List<DisplayTarget> Displays)
{
    public int SchemaVersion { get; init; } = 1;
}
public record DesktopSnapshot(List<DisplayInfo> Displays, string NativeData);
public record OperationResult(bool Success, string Message)
{
    public static OperationResult Ok(string message = "操作成功") => new(true, message);
    public static OperationResult Fail(string message) => new(false, message);
}
public interface IDisplayService
{
    DesktopSnapshot Capture();
    OperationResult Validate(DisplayProfile profile);
    OperationResult Apply(DisplayProfile profile, bool persist);
    OperationResult Restore(DesktopSnapshot snapshot);
}

/// <summary>Read-only verification that allows a native display transition to settle.</summary>
public interface IStableDisplayVerification
{
    OperationResult Verify(DisplayProfile profile);
}
