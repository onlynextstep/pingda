using System.Reflection;
using System.Windows;

namespace PingXu.App.Tests;

internal static class WindowBoundsChecks
{
    public static void Register(Action<string, Action> test)
    {
        test("最大化使用当前屏幕工作区，支持负坐标和各侧任务栏", () =>
        {
            var type = typeof(MainWindow).Assembly.GetType("PingXu.App.WindowBounds");
            var calculate = type?.GetMethod("Calculate", BindingFlags.Public | BindingFlags.Static);
            if (calculate == null) throw new Exception("没有按显示器工作区计算最大化边界");
            foreach (var (monitor, work, expected) in new[] {
                (new Rect(0,0,3440,1440), new Rect(0,0,3440,1392), new Rect(0,0,3440,1392)),
                (new Rect(-3840,0,3840,2160), new Rect(-3840,0,3840,2100), new Rect(0,0,3840,2100)),
                (new Rect(-2160,-3840,2160,3840), new Rect(-2100,-3840,2100,3840), new Rect(60,0,2100,3840)),
                (new Rect(3440,0,2560,1440), new Rect(3440,48,2560,1392), new Rect(0,48,2560,1392)),
                (new Rect(0,0,1920,1080), new Rect(0,0,1872,1080), new Rect(0,0,1872,1080)),
                (new Rect(0,0,3840,2160), new Rect(0,0,3840,2160), new Rect(0,0,3840,2160)) })
            {
                var actual = (Rect)calculate.Invoke(null, [monitor, work])!;
                if (actual != expected) throw new Exception($"最大化范围错误：{actual}，应为{expected}");
            }
        });
    }
}
