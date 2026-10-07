namespace PingXu.Core;

/// <summary>Live hardware limits, independent of the number of targets retained in a saved profile.</summary>
public static class DisplayLimits
{
    public const int MaximumConnected = 6;

    /// <summary>
    /// Check the full detected hardware list before offering layout changes or first-run defaults.
    /// Connected but disabled devices count; disconnected history does not. No records are changed or removed.
    /// A successful result checks only capacity, not identity, layout shape or native driver support.
    /// UI callers should display a failed result's Message and keep existing profiles intact.
    /// </summary>
    public static OperationResult CheckConnected(IReadOnlyList<DisplayInfo> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        if (displays.Any(d => d is null))
            return OperationResult.Fail("硬件列表包含空屏幕记录，请重新检测。");
        var connected = displays.Count(d => d.Connected);
        return connected > MaximumConnected
            ? OperationResult.Fail($"检测到 {connected} 块已连接屏幕，屏搭最多支持管理 {MaximumConnected} 块已连接屏幕。请断开多余屏幕后重新检测。")
            : OperationResult.Ok();
    }
}
