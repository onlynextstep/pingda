using System.Windows;
using System.Windows.Controls;

namespace PingXu.App.Tests;

internal static class SceneLayoutChecks
{
    public static void Check(WindowFixture f, bool extraCard)
    {
        if (f.Profiles.Count != 5) throw new Exception("回归用例需要五个默认预设");
        if (extraCard) f.Profiles.Add(f.Profiles[0] with { Id = "extra-scene", Name = "第六个预设" });
        bool previousRounding = f.Content.UseLayoutRounding;
        // Detaching MainWindow.Content loses this inherited Window property. Match the real UI.
        f.Content.UseLayoutRounding = f.Window.UseLayoutRounding;
        try
        {
            foreach (int width in new[] { 1488, 1487, 1489, 1400, 1060, 1488 })
            {
                VisualChecks.Layout(f, width, 1020);
                f.Invoke("RenderScenes");
                VisualChecks.Layout(f, width, 1020);
                var scroll = f.Get<ScrollViewer>("ScenesScroll");
                var strip = f.Get<StackPanel>("SceneStrip");
                var cards = strip.Children.OfType<Button>().ToArray();
                double right = cards[^1].TransformToAncestor(strip).Transform(new Point(cards[^1].ActualWidth, 0)).X;
                string detail = $"窗口 {width}，视口 {scroll.ViewportWidth}，内容 {scroll.ExtentWidth}，末卡右缘 {right}，可滚动 {scroll.ScrollableWidth}";
                if (!f.Content.UseLayoutRounding || cards.Length != (extraCard ? 6 : 5) || scroll.ViewportWidth <= 0)
                    throw new Exception("未覆盖真实五卡/六卡排版：" + detail);
                if (extraCard)
                {
                    if (scroll.ScrollableWidth <= 0 || scroll.ComputedHorizontalScrollBarVisibility != Visibility.Visible)
                        throw new Exception("第六张卡应保留横向滚动：" + detail);
                }
                else if (scroll.ScrollableWidth > .01 || right > scroll.ViewportWidth + .01
                    || scroll.ComputedHorizontalScrollBarVisibility == Visibility.Visible)
                    throw new Exception("五张卡产生了多余横向滚动：" + detail);
                Console.WriteLine(detail);
            }
        }
        finally { f.Content.UseLayoutRounding = previousRounding; }
    }
}
