using System.Diagnostics.CodeAnalysis;

namespace PingXu.Core;

/// <summary>Selects an unrotated mode for an attached display. Selection does not replace driver validation.</summary>
public static class DisplayEnableMode
{
    /// <summary>Preserves a valid active mode; otherwise uses the first advertised valid mode.
    /// The Windows adapter orders the target's preferred mode first. Inactive desktop dimensions are not used.</summary>
    public static bool TrySelect(DisplayInfo display, [NotNullWhen(true)] out DisplayMode? mode)
    {
        ArgumentNullException.ThrowIfNull(display);
        mode = null;
        if (!display.Connected) return false;
        if (display.Enabled && display.Width > 0 && display.Height > 0 && display.RefreshRate > 0 &&
            display.Rotation is 0 or 90 or 180 or 270)
        {
            var rotated = display.Rotation is 90 or 270;
            mode = new(rotated ? display.Height : display.Width, rotated ? display.Width : display.Height, display.RefreshRate);
            return true;
        }
        mode = display.Modes.FirstOrDefault(m => m.Width > 0 && m.Height > 0 && m.RefreshRate > 0);
        return mode is not null;
    }

    public static DisplayMode Select(DisplayInfo display)
    {
        if (TrySelect(display, out var mode)) return mode;
        throw new InvalidOperationException(display.Connected
            ? "Windows 已检测到显示器，但暂未提供可用的启用模式。请确认显示器已通电、输入源及连接线正常，再重新检测。"
            : "Windows 尚未检测到这台显示器的连接。请确认已通电、输入源及连接线正常，再重新检测。");
    }
}
