using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PingXu.App.Tests;

internal static class DisplayFailureTests
{
    internal static void Register(Action<string, Action> test)
    {
        const string original = "验证显示方案失败：Windows 拒绝了这次操作，具体原因尚未确定。（Windows 错误 5）";
        foreach (var safe in new[] { true, false })
            test("稳定性检查提示直接解释结果 safe=" + safe, () =>
            {
                var details = "未能确认所有屏幕已按预设启用。" + (safe ? "已恢复切换前的布局。" : "恢复失败。");
                var m = DisplayFailure.Create(details, safe ? DisplayFailureStage.SafeAfterError : DisplayFailureStage.UnknownAfterChange);
                Check(m.Explanation.Contains("未能确认所有屏幕"));
                if (safe) Check(m.Outcome.Contains("已恢复切换前") && m.NextStep.Contains("本地"));
                else Check(m.Outcome.Contains("无法确认") && m.NextStep.Contains("Windows 显示设置"));
            });
        foreach (string? desktop in new string?[] { null, "Default", "Unrecognized" })
            test("错误提示：未知原因不猜测环境 " + desktop, () =>
            {
                var m = DisplayFailure.Create(original, DisplayFailureStage.BeforeChange, desktop);
                Check(m.Explanation.Contains("具体原因尚未确定"));
                Check(!m.Explanation.Contains("远程") && !m.Explanation.Contains("屏保") && !m.Explanation.Contains("锁屏"));
                Check(m.Outcome == "本次没有更改屏幕布局。" && m.Details == original);
            });
        test("错误提示：实测屏保才解释屏保，不断言显示器关机", () =>
        {
            var m = DisplayFailure.Create(original, DisplayFailureStage.BeforeChange, "Screen-saver");
            Check(m.Explanation.Contains("屏幕保护程序") && m.NextStep.Contains("如果出现登录界面"));
            Check(!m.Explanation.Contains("电源") && !m.Explanation.Contains("关闭了显示器"));
        });
        test("错误提示：Winlogon不武断等同用户锁屏", () =>
        {
            var m = DisplayFailure.Create(original, DisplayFailureStage.BeforeChange, "Winlogon");
            Check(m.Explanation.Contains("不在普通桌面") && !m.Explanation.Contains("已锁屏"));
        });
        test("错误提示：参数错误不归因屏保", () =>
        {
            var m = DisplayFailure.Create("验证失败（Windows 错误 87）", DisplayFailureStage.BeforeChange, "Screen-saver");
            Check(m.Explanation.Contains("参数") && !m.Explanation.Contains("屏保"));
        });
        test("错误提示：结果未知不能声称未改变或已恢复", () =>
        {
            var m = DisplayFailure.Create(original, DisplayFailureStage.UnknownAfterChange);
            Check(m.Outcome.Contains("无法确认") && !m.Outcome.Contains("没有更改"));
            Check(m.NextStep.Contains("Windows 显示设置") && m.NextStep.Contains("检查恢复保护"));
        });
        test("错误提示：安全结束不臆造已恢复或未改动", () =>
        {
            var m = DisplayFailure.Create(original, DisplayFailureStage.SafeAfterError);
            Check(m.Outcome.Contains("状态已确认") && !m.Outcome.Contains("已恢复") && !m.Outcome.Contains("没有更改"));
        });
        test("错误提示：实际控件离屏预览", () =>
        {
            var message = DisplayFailure.Create(original, DisplayFailureStage.BeforeChange);
            var root = Dialogs.CreateFailureContent(message.Title, message.Explanation, message.Outcome,
                message.NextStep, message.Details, () => { });
            root.Measure(new Size(570, 420)); root.Arrange(new Rect(0, 0, 570, 420)); root.UpdateLayout();
            Check(PresentationSource.FromVisual(root) == null);
            var bitmap = new RenderTargetBitmap(570, 420, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var folder = Path.Combine(AppContext.BaseDirectory, "copy-preview"); Directory.CreateDirectory(folder);
            using var stream = File.Create(Path.Combine(folder, "failure.png"));
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
        });
    }
    static void Check(bool value) { if (!value) throw new Exception("错误提示推断了未经确认的原因或状态"); }
}
