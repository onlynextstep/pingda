using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace PingXu.App;

public enum DisplayFailureStage { BeforeChange, SafeAfterError, UnknownAfterChange }
public record DisplayFailure(string Title, string Explanation, string Outcome, string NextStep, string Details)
{
    // Pure presentation. Only the caller knows whether any display write could have happened.
    public static DisplayFailure Create(string details, DisplayFailureStage stage, string? observedDesktop = null)
    {
        var match = Regex.Match(details, @"Windows 错误 (\d+)");
        int? code = match.Success && int.TryParse(match.Groups[1].Value, out int value) ? value : null;
        string explanation = code switch
        {
            5 => "Windows 拒绝了这次操作，具体原因尚未确定。",
            50 => "当前显卡驱动不支持程序使用的屏幕设置功能。",
            87 => "Windows 未接受本次屏幕设置参数。",
            122 => "检测过程中显示器连接或设置发生了变化。",
            1610 => "Windows 无法使用所选的屏幕组合、分辨率和刷新率。",
            31 => "显卡驱动未能完成这次操作，具体原因尚未确定。",
            _ => "操作未能完成。具体错误保留在下方“错误详情”中。"
        };
        string next = "请点击“刷新显示器”后重试。如果仍然失败，请提供下方的错误详情。";
        bool unstable = details.StartsWith("未能确认所有屏幕已按预设启用", StringComparison.Ordinal);
        if (unstable)
        {
            explanation = "未能确认所有屏幕已按预设启用。";
            next = "可刷新显示器后重试。若再次失败，请提供本地 errors.log，便于检查切换时的屏幕状态。";
        }
        if (code == 5 && string.Equals(observedDesktop, "Screen-saver", StringComparison.OrdinalIgnoreCase))
        {
            explanation = "检测到 Windows 正在显示屏幕保护程序，暂时无法切换屏幕。";
            next = "请退出屏保；如果出现登录界面，请先解锁，再回到屏搭重试。";
        }
        else if (code == 5 && string.Equals(observedDesktop, "Winlogon", StringComparison.OrdinalIgnoreCase))
        {
            explanation = "Windows 当前不在普通桌面，暂时无法切换屏幕。";
            next = "请先返回 Windows 桌面；如果需要登录或解锁，请手动完成后再重试。";
        }
        string outcome = stage switch
        {
            DisplayFailureStage.BeforeChange => "本次没有更改屏幕布局。",
            DisplayFailureStage.SafeAfterError => "本次切换未完成，当前屏幕状态已确认，可以重试。",
            _ => "无法确认原布局是否已恢复，已暂停继续切换。"
        };
        if (stage == DisplayFailureStage.UnknownAfterChange)
            next = "请先在 Windows 显示设置中检查屏幕是否正常，再到屏搭“设置 → 检查恢复保护”中处理。";
        if (stage == DisplayFailureStage.SafeAfterError && details.EndsWith("已恢复切换前的布局。", StringComparison.Ordinal))
            outcome = "已恢复切换前的布局。";
        return new("未能切换屏幕", explanation, outcome, next, details);
    }
}

internal static class InputDesktopObservation
{
    // Read-only observation; never switches desktops, dismisses screen savers or changes access rights.
    internal static string? Read()
    {
        var desktop = OpenInputDesktop(0, false, 1); // DESKTOP_READOBJECTS
        if (desktop == IntPtr.Zero) return null;
        try
        {
            var name = new StringBuilder(256);
            return GetUserObjectInformation(desktop, 2, name, 512, out _) ? name.ToString() : null;
        }
        finally { CloseDesktop(desktop); }
    }
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder info, uint length, out uint needed);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr handle);
}
