namespace PingXu.Windows;

internal static class DisplayConversion
{
    // Swap between unrotated mode dimensions and physical desktop dimensions.
    // DisplayInfo/DisplayTarget Width/Height and X/Y describe the ROTATED desktop; Modes are unrotated.
    public static (int Width, int Height) DesktopSize(int width, int height, int rotation) => rotation switch
    {
        0 or 180 => (width, height),
        90 or 270 => (height, width),
        _ => throw new ArgumentException("方向必须为 0、90、180 或 270 度。")
    };
    internal static int Degrees(uint rotation) => rotation switch
    {
        1 => 0, 2 => 90, 3 => 180, 4 => 270,
        _ => throw new InvalidOperationException("Windows 返回了不支持的显示方向。")
    };
    internal static uint Rotation(int degrees)
    {
        _ = DesktopSize(1, 1, degrees);
        return (uint)(degrees / 90 + 1);
    }
    internal static int Hertz(Rational rate) => rate.Denominator == 0 ? 0
        : checked((int)Math.Round((double)rate.Numerator / rate.Denominator, MidpointRounding.AwayFromZero));
    internal static PingXu.Core.DisplayMode? FromDevMode(DevMode mode)
    {
        if (mode.Width == 0 || mode.Height == 0 || mode.Frequency <= 1 || mode.BitsPerPel != 32 || (mode.DisplayFlags & 2) != 0)
            return null;
        var rotation = (mode.Fields & 0x80) != 0 ? checked((int)mode.Orientation * 90) : 0;
        var (w, h) = DesktopSize(checked((int)mode.Width), checked((int)mode.Height), rotation);
        return new(w, h, checked((int)mode.Frequency));
    }
}
