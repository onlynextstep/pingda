namespace PingXu.Windows;

internal static class NativeErrors
{
    internal static void Check(int code, string action)
    {
        if (code == 0) return;
        var detail = code switch
        {
            5 => "Windows 拒绝了这次操作，具体原因尚未确定。",
            50 => "当前显卡驱动不支持程序使用的屏幕设置功能。",
            87 => "Windows 未接受本次屏幕设置参数。请刷新显示器后重试。",
            122 => "检测过程中显示器连接或设置发生了变化。请稍后刷新显示器。",
            1610 => "Windows 无法使用所选的屏幕组合、分辨率和刷新率。",
            31 => "显卡驱动未能完成这次操作，具体原因尚未确定。",
            _ => "Windows 未能完成这次操作，具体原因尚未确定。"
        };
        throw new InvalidOperationException($"{action}失败：{detail}（Windows 错误 {code}）");
    }
}
