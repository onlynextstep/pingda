using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.IO;
using System.Runtime.CompilerServices;
using DrawingPath = System.Windows.Shapes.Path;

namespace PingXu.App.Tests;

internal static class StudioAnnotationChecks
{
    public static void Check(WindowFixture f)
    {
        bool previousRounding = f.Content.UseLayoutRounding;
        f.Content.UseLayoutRounding = f.Window.UseLayoutRounding;
        try
        {
            var report = new List<string> { "离屏96 DPI：尺寸为原始像素；字号为DIP；仅内存假硬件。" };
            foreach (var (width, height) in new[] { (1360, 1020), (1488, 1020), (1800, 1020), (3440, 1440), (3840, 2160), (1060, 720) })
            {
                if (width >= 3440)
                    Console.WriteLine(f.Render(Path.Combine(ArtifactDirectory(), $"ui-studio-{width}x{height}.png"), width, height, 0));
                VisualChecks.Layout(f, width, height);
                f.Invoke("RenderCanvas");
                VisualChecks.Layout(f, width, height);
                var canvas = f.Get<Canvas>("LayoutCanvas");
                var sameSizeNumbers = new List<(double Width, double Height, double NumberHeight)>();
                foreach (var target in f.Draft.Displays.Where(d => d.Enabled))
                {
                    var screen = VisualChecks.Descendants(canvas).OfType<Button>().Single(b => Equals(b.Tag, target.Id));
                    var elements = VisualChecks.Descendants(screen).OfType<FrameworkElement>().ToArray();
                    FrameworkElement Find(string prefix) => elements.Single(e => AutomationProperties.GetAutomationId(e) == prefix + target.Id);
                    var number = (Image)Find("Studio.Number:");
                    sameSizeNumbers.Add((Math.Round(screen.Width, 3), Math.Round(screen.Height, 3), number.Height));
                    if (number.Source is not System.Windows.Media.Imaging.BitmapSource { PixelHeight: >= 400 })
                        throw new Exception($"{width}px 窗口的 {target.Id} 未使用高分辨率编号原图");
                    if (width == 1060)
                    {
                        double lastBottom = 0;
                        foreach (var annotation in ((Canvas)screen.Content).Children.OfType<FrameworkElement>())
                        {
                            var bounds = annotation.TransformToAncestor(screen).TransformBounds(new Rect(annotation.RenderSize));
                            var frame = new Rect(screen.RenderSize); frame.Inflate(.1, .1);
                            if (!frame.Contains(bounds) || bounds.Top < lastBottom - .1)
                                throw new Exception("回到最小窗口后标注溢出或相互遮挡");
                            lastBottom = bounds.Bottom;
                        }
                        if (target.Primary) _ = Find("Studio.Primary:");
                        continue;
                    }
                    var resolution = (TextBlock)Find("Studio.Resolution:");
                    var ratio = (TextBlock)Find("Studio.Ratio:");
                    if (resolution.Text != $"{target.Width} × {target.Height}" || resolution.FontSize < 11 || ratio.FontSize < 11)
                        throw new Exception("分辨率/比例缺失或字号过小");
                    if (width == 1800 && (resolution.FontSize < 15 || ratio.FontSize < 14))
                        throw new Exception("大画布分辨率/比例没有响应放大");
                    if (width >= 3440 && (resolution.FontSize < 32 || ratio.FontSize < 29 || number.Height < 120))
                        throw new Exception("原始3440/UHD应用窗口中的字号或编号仍过小");
                    var sizes = $"{width}×{height} {target.Id}: 屏框{screen.Width:F1}×{screen.Height:F1}; 分辨率{resolution.FontSize:F2}; 比例{ratio.FontSize:F2}; 编号高{number.Height:F2}";
                    Console.WriteLine(sizes); report.Add(sizes);
                    foreach (var text in new[] { resolution, ratio })
                    {
                        var probe = new TextBlock { Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize };
                        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        if (probe.DesiredSize.Width > text.ActualWidth + .1 || probe.DesiredSize.Height > text.ActualHeight + .1)
                            throw new Exception($"{width}px 窗口的 {target.Id} 元数据实际文字被裁剪");
                    }
                    var ordered = new List<FrameworkElement> { number, resolution, ratio };
                    if (target.Primary) ordered.Add(Find("Studio.Primary:"));
                    double bottom = 0;
                    foreach (var element in ordered)
                    {
                        var bounds = element.TransformToAncestor(screen).TransformBounds(new Rect(element.RenderSize));
                        var frame = new Rect(screen.RenderSize); frame.Inflate(.1, .1);
                        if (!frame.Contains(bounds) || bounds.Top < bottom - .1)
                            throw new Exception($"{target.Id} 的 {AutomationProperties.GetAutomationId(element)} 溢出或重叠");
                        bottom = bounds.Bottom;
                    }
                    if (width == 1360 && target.Width == 3440 && (screen.Height < 103 || screen.Height > 108))
                        throw new Exception("没有覆盖约106px高的真实带鱼预览");
                    if (Math.Abs(screen.Width / screen.Height - (double)target.Width / target.Height) > .001)
                        throw new Exception("显示器矩形真实比例发生变化");
                }
                foreach (var group in sameSizeNumbers.GroupBy(n => (n.Width, n.Height)))
                    if (group.Max(n => n.NumberHeight) - group.Min(n => n.NumberHeight) > .01)
                        throw new Exception($"{width}×{height} 同尺寸屏框因主屏徽章导致编号高度不同");
            }
            File.WriteAllLines(Path.Combine(ArtifactDirectory(), "studio-font-sizes.txt"), report);
        }
        finally { f.Content.UseLayoutRounding = previousRounding; }
    }
    private static string ArtifactDirectory([CallerFilePath] string source = "") => Path.Combine(Path.GetDirectoryName(source)!, "artifacts");
}
