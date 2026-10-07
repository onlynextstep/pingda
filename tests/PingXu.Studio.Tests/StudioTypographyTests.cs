using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PingXu.App;
using PingXu.Core;
using DrawingPath = System.Windows.Shapes.Path;

namespace PingXu.Studio.Tests;

// Independently runnable with STUDIO_TYPOGRAPHY_TESTS + StartupObject; existing Program.cs stays untouched.
public static class StudioTypographyTests
{
    public static void Register(Action<string, Action> test)
    {
        test("选中角标在普通与大画布清晰可见且仍以屏幕角为中心", () =>
        {
            double previous = 0;
            foreach (int width in new[] { 1100, 3440 })
            {
                var canvas = Render(width, 1440, 3);
                var screen = Screen(canvas, "A");
                var marker = Find<System.Windows.Shapes.Rectangle>(canvas, "Studio.Corner:A:NE");
                Check(marker.Width >= 10 && marker.Width > previous, "选中角标过小或未随画布放大");
                previous = marker.Width;
                Near(Canvas.GetLeft(marker) + marker.Width / 2, Canvas.GetLeft(screen) + screen.Width);
                Near(Canvas.GetTop(marker) + marker.Height / 2, Canvas.GetTop(screen));
                Check(!marker.IsHitTestVisible, "装饰角标不应阻挡拖动");
            }
        });
        test("大屏编号视觉重心靠近屏框中线且保留元数据边界", () =>
        {
            foreach (var size in new[] { new Size(3440, 1440), new Size(3840, 2160) })
            {
                var canvas = Render(size.Width, size.Height, 3);
                foreach (string id in new[] { "A", "B", "C" })
                {
                    var screen = Screen(canvas, id);
                    var number = Find<Image>(canvas, "Studio.Number:" + id);
                    double center = (Canvas.GetTop(number) + number.Height / 2) / screen.Height;
                    Check(center >= .35 && center <= .46, $"{id} 编号重心偏上：{center:F3}");
                }
                CheckAnnotations(canvas);
            }
        });
        test("屏幕名称随大窗口放大且保留上方空间", () =>
        {
            foreach (int width in new[] { 1100, 3440, 3840 })
            {
                var canvas = Render(width, 900, 3);
                var label = Find<TextBlock>(canvas, "Studio.Label:B");
                Check(label.FontSize >= (width >= 3440 ? 28 : 15), "屏幕名称仍使用小窗口字号");
                Check(label.Height >= label.FontSize * 1.3, "名称行高不足");
                Check(Canvas.GetTop(label) >= 0 && Canvas.GetTop(label) + label.Height < Canvas.GetTop(Screen(canvas, "B")), "名称被裁剪或压住屏幕");
            }
        });
        test("01至06等比显示原图，前导零和微光留白完整", () =>
        {
            foreach (int value in Enumerable.Range(1, 6))
            {
                var canvas = Render(1100, 650, value);
                var number = Find<Image>(canvas, "Studio.Number:A");
                Near(number.Height, 70);
                Check(number.Source is BitmapSource, "未使用用户原图");
                Near(number.Width / number.Height, number.Source.Width / number.Source.Height);
                Check(AutomationProperties.GetName(number).EndsWith(value.ToString("00")), "前导零丢失");
            }
        });
        test("大画布元数据响应放大，106px带鱼与主屏不裁剪", () =>
        {
            double previous = 0;
            foreach (int width in new[] { 900, 1100, 1600 })
            {
                var canvas = Render(width, 900, 3);
                var resolution = Find<TextBlock>(canvas, "Studio.Resolution:B");
                var ratio = Find<TextBlock>(canvas, "Studio.Ratio:B");
                Check(resolution.FontSize > previous, "预览扩大却未放大分辨率文字"); previous = resolution.FontSize;
                if (width == 900) Near(Screen(canvas, "B").Height, 106.119402985, .01);
                if (width == 1600) Check(resolution.FontSize >= 17 && ratio.FontSize >= 15, "大画布元数据仍过小");
                CheckAnnotations(canvas);
            }
        });
        test("多位编号、横竖屏、小画布和主屏保持真实比例及边界", () =>
        {
            foreach (var size in new[] { new Size(400, 300), new Size(900, 492), new Size(1600, 900),
                new Size(3440, 1440), new Size(3840, 2160), new Size(160, 100) })
            foreach (int number in new[] { 1, 12, 123, int.MaxValue })
            foreach (bool portrait in new[] { false, true })
            {
                var canvas = Render(size.Width, size.Height, number, portrait);
                CheckAnnotations(canvas);
                var a = Screen(canvas, "A"); var b = Screen(canvas, "B");
                Near(a.Width / a.Height, portrait ? 2160d / 3840 : 3840d / 2160);
                Near(b.Width / b.Height, 3440d / 1440);
                Near(a.Width / (portrait ? 2160 : 3840), b.Width / 3440);
                Check(AutomationProperties.GetName(Find<Image>(canvas, "Studio.Number:A")).EndsWith(number.ToString("00")), "多位编号被截短");
            }
        });
        test("3440/3840原尺寸元数据继续增大，编号和主屏同步保持层级", () =>
        {
            double previous = 0;
            foreach (var size in new[] { new Size(1600, 900), new Size(3440, 1440), new Size(3840, 2160) })
            {
                var canvas = Render(size.Width, size.Height, 3);
                CheckAnnotations(canvas);
                var resolution = Find<TextBlock>(canvas, "Studio.Resolution:B");
                var ratio = Find<TextBlock>(canvas, "Studio.Ratio:B");
                Check(resolution.FontSize > previous || (previous == 48 && resolution.FontSize == 48), "超宽/UHD画布字号提前触顶"); previous = resolution.FontSize;
                if (size.Width >= 3440)
                {
                    Check(resolution.FontSize >= 36 && ratio.FontSize >= 33, "原尺寸大画布文字仍停留在小窗口字号");
                    Check(Find<Image>(canvas, "Studio.Number:B").Height >= 140, "编号没有随元数据放大");
                    var badge = Find<Viewbox>(canvas, "Studio.Primary:B");
                    var label = Descendants(badge).OfType<TextBlock>().Single(t => t.Text == "主屏");
                    double scale = label.TransformToAncestor(canvas).TransformBounds(new Rect(0, 0, 1, 1)).Height;
                    Check(label.FontSize * scale >= 18, "大画布主屏仍不足18 DIP");
                    Save(canvas, $"typography-{size.Width}x{size.Height}.png");
                }
            }
        });
        test("大屏后回到最小屏：各主屏位置与106px边界无标注相交", () =>
        {
            foreach (string primary in new[] { "A", "B", "C" })
            foreach (int width in new[] { 3840, 920, 900, 880, 400, 160 })
            {
                var canvas = Render(width, width == 3840 ? 2160 : width >= 880 ? 492 : width == 400 ? 300 : 100, 123, primary: primary);
                CheckAnnotations(canvas);
                var badge = Find<Viewbox>(canvas, "Studio.Primary:" + primary);
                Check(badge.ActualWidth > 0 && badge.ActualHeight > 0, "主屏标记消失");
                if (width is >= 880 and <= 920)
                {
                    foreach (string id in new[] { "A", "B", "C" })
                    {
                        _ = Find<TextBlock>(canvas, "Studio.Resolution:" + id);
                        _ = Find<TextBlock>(canvas, "Studio.Ratio:" + id);
                    }
                }
            }
        });
        test("主屏在任意位置：同尺寸屏框同号的高度宽度和点密度一致", () =>
        {
            foreach (string primary in new[] { "A", "B", "C" })
            foreach (var size in new[] { new Size(400, 300), new Size(880, 492), new Size(900, 492),
                new Size(1026, 530), new Size(3440, 1440), new Size(3840, 2160) })
            foreach (int number in new[] { 1, 2, 3, 12, 123, int.MaxValue })
            {
                var canvas = Render(size.Width, size.Height, number, primary: primary, uniformNumber: number);
                var left = Find<Image>(canvas, "Studio.Number:B");
                var right = Find<Image>(canvas, "Studio.Number:C");
                Near(Screen(canvas, "B").Width, Screen(canvas, "C").Width);
                Near(Screen(canvas, "B").Height, Screen(canvas, "C").Height);
                Near(left.Height, right.Height); Near(left.Width, right.Width);
                Near(left.Source.Height, right.Source.Height);
                Check(ReferenceEquals(left.Source, right.Source), "同一个编号未复用缓存素材");
                CheckAnnotations(canvas);
                if (size.Width is >= 880 and <= 1026)
                    foreach (string id in new[] { "B", "C" })
                    {
                        _ = Find<TextBlock>(canvas, "Studio.Resolution:" + id);
                        _ = Find<TextBlock>(canvas, "Studio.Ratio:" + id);
                        if (number < 100) Check(Find<Image>(canvas, "Studio.Number:" + id).Height >= 28, "106px编号过度缩小");
                    }
            }
        });
        test("离屏位图点阵有可辨亮点，生成全数字与大小画布证据", () =>
        {
            foreach (var size in new[] { new Size(900, 492), new Size(1600, 900) })
            {
                var canvas = Render(size.Width, size.Height, 3);
                Save(canvas, $"typography-{size.Width}x{size.Height}.png");
                var number = Find<Image>(canvas, "Studio.Number:B");
                var bitmap = Bitmap(canvas);
                var bounds = number.TransformToAncestor(canvas).TransformBounds(new Rect(number.RenderSize));
                int x = (int)Math.Ceiling(bounds.X), y = (int)Math.Ceiling(bounds.Y);
                int w = (int)Math.Floor(bounds.Right) - x, h = (int)Math.Floor(bounds.Bottom) - y;
                var pixels = new byte[w * h * 4]; bitmap.CopyPixels(new Int32Rect(x, y, w, h), pixels, w * 4, 0);
                int bright = 0, dark = 0;
                for (int i = 0; i < pixels.Length; i += 4) { if (pixels[i] > 100) bright++; if (pixels[i] < 70) dark++; }
                Check(bright > 40 && dark > 40, "点阵栅格化后没有可辨墨迹或空隙");
            }
            var atlas = new Canvas { Width = 1100, Height = 1040, Background = new SolidColorBrush(Color.FromRgb(21, 22, 22)) };
            for (int digit = 0; digit < 6; digit++)
            {
                var sample = Render(1100, 650, 1 + digit);
                var source = Find<Image>(sample, "Studio.Number:A");
                var glyph = new Image { Source = source.Source, Width = source.Width * 1.6, Height = source.Height * 1.6, Stretch = Stretch.Uniform };
                Canvas.SetLeft(glyph, 50 + digit % 3 * 350); Canvas.SetTop(glyph, 35 + digit / 3 * 160); atlas.Children.Add(glyph);
            }
            var large = Render(1100, 650, 3); Canvas.SetTop(large, 370); atlas.Children.Add(large);
            Layout(atlas); Save(atlas, "typography-glyph-atlas.png");
            Check(Application.Current == null && PresentationSource.FromVisual(atlas) == null, "测试连接了桌面窗口");
        });
    }

    private static Canvas Render(double width, double height, int number, bool portrait = false, string primary = "B", int? uniformNumber = null)
    {
        int w = portrait ? 2160 : 3840, h = portrait ? 3840 : 2160;
        var profile = new DisplayProfile("fake", "假三屏", [new("A", true, false, 0, 0, w, h, portrait ? 90 : 0, 60),
            new("B", true, true, w, 0, 3440, 1440, 0, 60), new("C", true, false, w + 3440, 0, 3440, 1440, 0, 60)]);
        for (int i = 0; i < profile.Displays.Count; i++) profile.Displays[i] = profile.Displays[i] with { Primary = profile.Displays[i].Id == primary };
        var canvas = new Canvas { Width = width, Height = height }; Layout(canvas);
        StudioDrawing.DrawLayout(canvas, profile, [], new Dictionary<string, string> { ["A"] = "左侧4K", ["B"] = "中间带鱼", ["C"] = "右侧带鱼" },
            new Dictionary<string, int> { ["A"] = number, ["B"] = uniformNumber ?? 1, ["C"] = uniformNumber ?? 2 }, "A", _ => throw new Exception("绘制不应调用选择"));
        Layout(canvas); return canvas;
    }
    private static void CheckAnnotations(Canvas canvas)
    {
        foreach (var screen in Descendants(canvas).OfType<Button>())
        {
            double bottom = 0;
            foreach (var element in ((Canvas)screen.Content).Children.OfType<FrameworkElement>())
            {
                var bounds = element.TransformToAncestor(screen).TransformBounds(new Rect(element.RenderSize));
                var frame = new Rect(screen.RenderSize); frame.Inflate(.1, .1);
                Check(frame.Contains(bounds) && bounds.Top >= bottom - .1, "文字/编号/主屏越界或重叠：" + AutomationProperties.GetAutomationId(element));
                bottom = bounds.Bottom;
                if (element is TextBlock text)
                {
                    var probe = new TextBlock { Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize };
                    probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Check(probe.DesiredSize.Width <= text.ActualWidth + .1 && probe.DesiredSize.Height <= text.ActualHeight + .1, "文字实际墨迹被裁剪");
                }
            }
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static T Find<T>(Canvas canvas, string id) where T : FrameworkElement => Descendants(canvas).OfType<T>().Single(e => AutomationProperties.GetAutomationId(e) == id);
    private static Button Screen(Canvas canvas, string id) => Find<Button>(canvas, "Studio.Display:" + id);
    private static void Layout(Canvas canvas) { canvas.Measure(new Size(canvas.Width, canvas.Height)); canvas.Arrange(new Rect(canvas.DesiredSize)); canvas.UpdateLayout(); }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(double a, double b, double epsilon = .001) => Check(Math.Abs(a - b) < epsilon, $"预期{b}，实际{a}");
    private static RenderTargetBitmap Bitmap(Canvas canvas)
    { var bitmap = new RenderTargetBitmap((int)canvas.Width, (int)canvas.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas); return bitmap; }
    private static void Save(Canvas canvas, string name)
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SourcePath())!, "artifacts"); Directory.CreateDirectory(folder);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Bitmap(canvas)));
        using var file = File.Create(System.IO.Path.Combine(folder, name)); encoder.Save(file);
    }
    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
#if STUDIO_TYPOGRAPHY_TESTS
    [STAThread]
    public static int Main()
    {
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        int passed = 0, failed = 0;
        Register((name, body) => { try { body(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.GetBaseException().Message); } });
        Console.WriteLine($"Typography: {passed} passed, {failed} failed; no Application/Window/native backend.");
        return failed == 0 ? 0 : 1;
    }
#endif
}
