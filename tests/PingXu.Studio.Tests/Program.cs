using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PingXu.Core;
using DrawingPath = System.Windows.Shapes.Path;

namespace PingXu.Studio.Tests;

internal static class Program
{
    private delegate void Draw(Canvas canvas, DisplayProfile draft, IReadOnlyList<DisplayInfo> live,
        IReadOnlyDictionary<string, string> labels, IReadOnlyDictionary<string, int> numbers,
        string? selected, Action<string> select);

    private static Draw Drawing => (typeof(Program).Assembly.GetType("PingXu.App.StudioDrawing")
        ?.GetMethod("DrawLayout", BindingFlags.Public | BindingFlags.Static)
        ?? throw new Exception("缺少生产 API PingXu.App.StudioDrawing.DrawLayout"))
        .CreateDelegate<Draw>();

    // All display information is fictitious test data; there is no hardware service or Window.
    private static DisplayProfile Fixture() => new("test", "模拟三屏", [
        new("LEFT", true, false, -3840, -720, 3840, 2160, 0, 60),
        new("MAIN", true, true, 0, 0, 3440, 1440, 0, 60),
        new("RIGHT", true, false, 3440, 0, 3440, 1440, 0, 60)]);

    private static readonly Dictionary<string, string> Labels = new()
    { ["left"] = "左侧4K", ["main"] = "中间带鱼", ["right"] = "右侧带鱼" };
    private static readonly Dictionary<string, int> Numbers = new()
    { ["left"] = 3, ["main"] = 1, ["right"] = 2 };

    private static List<DisplayInfo> Live(DisplayProfile profile) => profile.Displays.Select(d =>
        new DisplayInfo(d.Id, "FAKE_" + d.Id, "模拟硬件 " + d.Id, true, d.Enabled, d.Primary,
            d.X, d.Y, d.Width, d.Height, d.Rotation, d.RefreshRate,
            new[] { new DisplayMode(d.Width, d.Height, d.RefreshRate) })).ToList();

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 0) { Console.Error.WriteLine("测试不接受参数或真实硬件模式。"); return 2; }
        Console.OutputEncoding = Encoding.UTF8;
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        int failed = 0, passed = 0;
        void Test(string name, Action body)
        {
            try { body(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.GetBaseException().Message); }
        }

        NumberArtworkTests.Register(Test);
        StudioTypographyTests.Register(Test);
        Test("公共静态 API 可绑定；测试无需 Application/Window/硬件后端", () =>
        {
            _ = Drawing;
            Check(Application.Current == null, "不应创建 Application");
            Check(!typeof(Program).Assembly.GetReferencedAssemblies().Any(a => a.Name is "PingXu" or "PingXu.Windows"), "隔离项目引用了 App 或原生后端");
        });

        // Catches independent X/Y scaling, invented gaps, clamping or double-rotation of screen geometry.
        foreach (var (width, height) in new[] { (400, 300), (700, 450), (1100, 650) })
            Test($"{width}×{height} 真实宽高、负坐标、接缝共用一个比例", () =>
            {
                var c = Render(Fixture(), width, height);
                var a = Screen(c, "LEFT"); var b = Screen(c, "MAIN"); var r = Screen(c, "RIGHT");
                double scale = a.Width / 3840;
                Near(a.Height, 2160 * scale); Near(b.Width, 3440 * scale); Near(b.Height, 1440 * scale);
                Near(Canvas.GetLeft(b) - Canvas.GetLeft(a), 3840 * scale);
                Near(Canvas.GetTop(a) - Canvas.GetTop(b), -720 * scale);
                Near(Canvas.GetLeft(r), Canvas.GetLeft(b) + b.Width);
                foreach (var button in new[] { a, b, r }) Inside(c, button);
            });

        Test("竖屏直接使用草稿宽高，坐标空隙仍按真实像素映射", () =>
        {
            var p = Fixture();
            p.Displays[0] = p.Displays[0] with { Width = 2160, Height = 3840, Rotation = 90, X = -3000, Y = -1080 };
            var c = Render(p, 400, 300); var a = Screen(c, "LEFT"); var b = Screen(c, "MAIN");
            Near(a.Width / a.Height, .5625);
            Near(Canvas.GetLeft(b) - Canvas.GetLeft(a), a.Width / 2160 * 3000);
            Inside(c, Number(c, "LEFT")); Inside(c, Primary(c, "MAIN"));
        });

        // Catches a screen-index closure, fallback action, state mutation or accumulated redraw handlers.
        Test("点击每屏仅回调原始 ID；重绘不重复回调，不修改输入", () =>
        {
            var p = Fixture(); var live = Live(p); var hits = new List<string>();
            var before = JsonSerializer.Serialize(new { p, live, Labels, Numbers });
            var c = MakeCanvas(700, 450);
            Drawing(c, p, live, Labels, Numbers, "left", hits.Add); Layout(c);
            foreach (var id in new[] { "RIGHT", "MAIN", "LEFT" }) Screen(c, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(hits.SequenceEqual(new[] { "RIGHT", "MAIN", "LEFT" }), "回调次数或 ID 不正确");
            Drawing(c, p, live, Labels, Numbers, "main", hits.Add); Layout(c);
            Screen(c, "LEFT").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(hits.Count == 4 && hits[^1] == "LEFT", "重绘累积回调");
            Check(before == JsonSerializer.Serialize(new { p, live, Labels, Numbers }), "绘制或选择修改了输入");
            Check(Descendants(c).OfType<Button>().Count() == 3, "重绘遗留显示器");
        });

        Test("外置中文名、分辨率、精确比例、主屏与离线状态可见且可访问", () =>
        {
            var p = Fixture(); var live = Live(p); live[0] = live[0] with { Connected = false };
            var c = MakeCanvas(1100, 650); Drawing(c, p, live, Labels, Numbers, "left", _ => { }); Layout(c);
            var name = Element(c, "Studio.Label:LEFT"); var screen = Screen(c, "LEFT");
            Check(name is TextBlock { Text: "左侧4K" }, "未使用不区分大小写的中文别名");
            Check(Bounds(c, name).Bottom <= Bounds(c, screen).Top, "名称应在显示器外上方");
            var texts = Descendants(screen).OfType<TextBlock>().Select(t => t.Text).ToList();
            Check(texts.Contains("3840 × 2160") && texts.Contains("16 : 9"), "缺少分辨率或正确宽高比");
            Check(Descendants(Screen(c, "MAIN")).OfType<TextBlock>().Any(t => t.Text == "43 : 18"), "带鱼屏应标注真实比例");
            Check(Primary(c, "MAIN") is FrameworkElement, "未标注主屏");
            foreach (var b in Descendants(c).OfType<Button>())
                Check(b.ToolTip is string tip && tip.Length > 0 && !string.IsNullOrWhiteSpace(AutomationProperties.GetName(b)), "按钮缺少提示或辅助名称");
            Check(screen.ToolTip!.ToString()!.Contains("未连接"), "未连接状态被伪装为在线");
            Check(screen.ToolTip!.ToString()!.Contains("-3840") && screen.ToolTip!.ToString()!.Contains("-720"), "提示丢失负坐标");
        });

        // Tick labels must locate their real world coordinate, even under a non-Chinese process culture.
        Test("中文坐标标尺与网格跟随世界坐标，负刻度和零点准确", () =>
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
                var p = Fixture(); p.Displays[0] = p.Displays[0] with { Y = -2160 };
                var c = Render(p, 1100, 650); var main = Screen(c, "MAIN");
                double scale = main.Width / 3440;
                var ticks = Descendants(c).OfType<TextBlock>().Where(t => AutomationProperties.GetAutomationId(t).StartsWith("Studio.Tick.")).ToList();
                Check(ticks.Count > 5 && ticks.Any(t => t.Text.StartsWith('-')), "负坐标布局缺少真实负刻度");
                Check(ticks.Any(t => t.Text == "0"), "缺少零刻度");
                var captions = Descendants(c).OfType<TextBlock>().Select(t => t.Text).ToList();
                Check(captions.Contains("X（像素）") && captions.Contains("Y（像素）"), "缺少中文标尺单位");
                foreach (var t in ticks)
                {
                    Check(double.TryParse(t.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value), "刻度应是可验证的真实像素数");
                    bool x = AutomationProperties.GetAutomationId(t).StartsWith("Studio.Tick.X:");
                    var box = Bounds(c, t);
                    double position = x ? box.Left + box.Width / 2 : box.Top + box.Height / 2;
                    Near(position, (x ? Canvas.GetLeft(main) : Canvas.GetTop(main)) + value * scale, .02);
                    Inside(c, t);
                    Check(Descendants(c).OfType<Line>().Any(l => AutomationProperties.GetAutomationId(l).StartsWith(x ? "Studio.Grid.X:" : "Studio.Grid.Y:")
                        && Math.Abs((x ? l.X1 : l.Y1) - position) < .02), "刻度没有对应的世界网格线");
                }
            }
            finally { CultureInfo.CurrentCulture = previous; }
        });

        Test("选中仅酸黄细边与两个对角实方，屏幕保持暗炭黑", () =>
        {
            var c = Render(Fixture(), 1100, 650); var b = Screen(c, "LEFT");
            Check(b.BorderBrush is SolidColorBrush acid && acid.Color == Color.FromRgb(220, 255, 66), "选中边界不是 #DCFF42");
            Check(b.Background is SolidColorBrush bg && Math.Max(bg.Color.R, Math.Max(bg.Color.G, bg.Color.B)) < 45
                && Math.Abs(bg.Color.R - bg.Color.G) <= 3, "显示器有大块彩色填充");
            var corners = Descendants(c).OfType<Rectangle>().Where(r => AutomationProperties.GetAutomationId(r).StartsWith("Studio.Corner:LEFT:")).ToList();
            Check(corners.Count == 2, "应有两个选中角标");
            var screen = Bounds(c, b);
            var centers = corners.Select(r => { var q = Bounds(c, r); return new Point(q.Left + q.Width / 2, q.Top + q.Height / 2); }).ToList();
            Check(centers.Any(p => Math.Abs(p.X - screen.Right) < .1 && Math.Abs(p.Y - screen.Top) < .1)
                && centers.Any(p => Math.Abs(p.X - screen.Left) < .1 && Math.Abs(p.Y - screen.Bottom) < .1), "角标未落在右上/左下");
            foreach (var corner in corners)
                Check(corner.Width == corner.Height && corner.Width <= 16 && corner.Fill is SolidColorBrush ink && ink.Color == Color.FromRgb(220, 255, 66), "角标不是适度大小的主题色实方");
        });

        // Checks renderable vector ink and full bounds, not a hidden text or test-only property.
        foreach (var (width, height) in new[] { (400, 300), (1100, 650), (160, 100) })
            Test($"{width}×{height} 点阵编号与主屏完整可见", () =>
            {
                var c = Render(Fixture(), width, height);
                foreach (var (id, text) in new[] { ("LEFT", "03"), ("MAIN", "01"), ("RIGHT", "02") })
                {
                    var n = Number(c, id);
                    Check(AutomationProperties.GetName(n).Contains(text), "点阵编号不正确");
                    Check(n.Source is BitmapSource { PixelHeight: >= 400 }, "编号必须使用完整清晰度的原图素材");
                    Check(n.Source != null && n.Visibility == Visibility.Visible && n.Opacity > 0 && n.ActualWidth > 0 && n.ActualHeight > 0, "点阵没有可见尺寸或墨迹");
                    Inside(c, n); Contains(Bounds(c, Screen(c, id)), Bounds(c, n));
                }
                var primary = Primary(c, "MAIN"); Inside(c, primary);
                Contains(Bounds(c, Screen(c, "MAIN")), Bounds(c, primary));
            });

        Test("约106px高的带鱼屏保留细微点阵和完整元数据，不改变屏幕比例", () =>
        {
            var p = Fixture(); p.Displays[0] = p.Displays[0] with { Y = 0 };
            foreach (int width in new[] { 880, 900, 920 })
            {
                var c = Render(p, width, 492);
                foreach (var target in p.Displays)
                {
                    var screen = Screen(c, target.Id); var number = Number(c, target.Id);
                    Near(screen.Width / screen.Height, (double)target.Width / target.Height);
                    Check(number.Source is BitmapSource { PixelHeight: >= 400 },
                        $"{width}px 画布 {target.Id} 编号退化为七行粗点阵");
                    var resolution = (TextBlock)Element(c, "Studio.Resolution:" + target.Id);
                    var ratio = (TextBlock)Element(c, "Studio.Ratio:" + target.Id);
                    Check(resolution.Text == $"{target.Width} × {target.Height}" && resolution.FontSize >= 11 && ratio.FontSize >= 11,
                        "元数据内容或可读字号不正确");
                    var ordered = new List<FrameworkElement> { number, resolution, ratio };
                    if (target.Primary) ordered.Add(Primary(c, target.Id));
                    double bottom = Bounds(c, screen).Top;
                    foreach (var element in ordered)
                    {
                        var bounds = Bounds(c, element); Contains(Bounds(c, screen), bounds);
                        Check(bounds.Top >= bottom - .1, "编号、分辨率、比例或主屏相互重叠");
                        bottom = bounds.Bottom;
                    }
                    Check(number.ActualHeight >= 28, "为了元数据过度压缩了编号");
                    foreach (var text in new[] { resolution, ratio })
                    {
                        var natural = new TextBlock { Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize };
                        natural.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        Check(natural.DesiredSize.Width <= text.ActualWidth && natural.DesiredSize.Height <= text.ActualHeight + .1,
                            "元数据字符串实际超出了分配区域");
                    }
                }
                Near(Canvas.GetLeft(Screen(c, "MAIN")), Canvas.GetLeft(Screen(c, "LEFT")) + Screen(c, "LEFT").Width);
                Near(Canvas.GetLeft(Screen(c, "RIGHT")), Canvas.GetLeft(Screen(c, "MAIN")) + Screen(c, "MAIN").Width);
                if (width == 900) Check(Screen(c, "MAIN").Height is > 105 and < 107, "未覆盖106px临界高度");
            }
        });

        Test("超出图片范围的历史编号显示文字且不会截断", () =>
        {
            for (int digit = 0; digit <= 9; digit++)
            {
                var c = MakeCanvas(400, 300);
                Drawing(c, Fixture(), Live(Fixture()), Labels, new Dictionary<string, int> { ["LEFT"] = 10 + digit }, null, _ => { }); Layout(c);
                var n = Number(c, "LEFT"); Check(AutomationProperties.GetName(n).Contains((10 + digit).ToString()), "数字标签不一致");
                // The lean "11" glyph has exactly twenty squares; it is still a complete two-digit number.
                Check(n.Source != null && n.Source.Width > 0 && n.Source.Height > 0, "不支持的编号应有可读文字回退");
            }
            var longCanvas = MakeCanvas(400, 300);
            Drawing(longCanvas, Fixture(), Live(Fixture()), Labels, new Dictionary<string, int> { ["MAIN"] = 1234567890 }, null, _ => { }); Layout(longCanvas);
            Contains(Bounds(longCanvas, Screen(longCanvas, "MAIN")), Bounds(longCanvas, Number(longCanvas, "MAIN")));
            Inside(longCanvas, Primary(longCanvas, "MAIN"));
        });

        Test("停用显示器底部仍可选择，且不改变活动屏幕的世界范围", () =>
        {
            var p = Fixture(); p.Displays.Add(new("OFF", false, false, int.MaxValue, int.MinValue, 1920, 1080, 0, 60));
            var hits = new List<string>(); var c = MakeCanvas(400, 300);
            Drawing(c, p, Live(p), Labels, Numbers, "OFF", hits.Add); Layout(c);
            var chip = Screen(c, "OFF"); Inside(c, chip);
            Check(chip.ToolTip is string tip && tip.Contains("已停用"), "停用状态不明确");
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(hits.SequenceEqual(new[] { "OFF" }), "停用 chip 无法选择");
            Check(Screen(c, "LEFT").Width > 80, "停用的极端坐标污染了布局范围");
        });

        Test("400×300 主屏中文保持可读尺寸，点阵在最终位图中有实际墨迹", () =>
        {
            var c = Render(Fixture(), 400, 300);
            var primaryText = Descendants(Primary(c, "MAIN")).OfType<TextBlock>().Single(t => t.Text == "主屏");
            var unit = primaryText.TransformToAncestor(c).TransformBounds(new Rect(0, 0, 1, 1));
            Check(primaryText.FontSize * unit.Height >= 8, "主屏中文被缩到不足 8 DIP，虽未截断却难以辨认");
            var bitmap = new RenderTargetBitmap(400, 300, 96, 96, PixelFormats.Pbgra32); bitmap.Render(c);
            foreach (var id in new[] { "LEFT", "MAIN", "RIGHT" })
            {
                var box = Bounds(c, Number(c, id));
                int x = (int)Math.Ceiling(box.Left), y = (int)Math.Ceiling(box.Top);
                int w = Math.Max(1, (int)Math.Floor(box.Right) - x), h = Math.Max(1, (int)Math.Floor(box.Bottom) - y);
                var pixels = new byte[w * h * 4]; bitmap.CopyPixels(new Int32Rect(x, y, w, h), pixels, w * 4, 0);
                int ink = 0;
                for (int i = 0; i < pixels.Length; i += 4) if (pixels[i] > 100 && pixels[i + 1] > 100 && pixels[i + 2] > 100) ink++;
                Check(ink >= 8, $"{id} 点阵只有对象，没有真正渲染的亮色方点");
            }
        });

        Test("多块停用屏幕出现水平滚动条时 chip 文字仍有完整高度", () =>
        {
            var p = Fixture();
            p.Displays[0] = p.Displays[0] with { Primary = true };
            p.Displays[1] = p.Displays[1] with { Enabled = false, Primary = false };
            p.Displays[2] = p.Displays[2] with { Enabled = false };
            var c = Render(p, 400, 300);
            var scroll = Descendants(c).OfType<ScrollViewer>().Single();
            Check(scroll.ComputedHorizontalScrollBarVisibility == Visibility.Visible, "测试未触发滚动条");
            Check(scroll.ViewportHeight >= Screen(c, "MAIN").ActualHeight, "滚动条挤占 chip，导致底部文字裁切");
            Inside(c, scroll);
        });

        Test("没有别名或编号时使用传入硬件信息；未知硬件不捏造身份", () =>
        {
            var p = Fixture(); var c = MakeCanvas(700, 450);
            Drawing(c, p, Live(p), new Dictionary<string, string>(), new Dictionary<string, int>(), null, _ => { }); Layout(c);
            Check(((TextBlock)Element(c, "Studio.Label:LEFT")).Text == "模拟硬件 LEFT", "别名缺失时丢失传入硬件名");
            Check(AutomationProperties.GetName(Number(c, "MAIN")).Contains("02"), "编号缺失时没有使用 live 中的枚举顺序");
            Drawing(c, p, Array.Empty<DisplayInfo>(), new Dictionary<string, string>(), new Dictionary<string, int>(), null, _ => { }); Layout(c);
            Check(((TextBlock)Element(c, "Studio.Label:LEFT")).Text == "LEFT", "未知硬件被捏造命名");
            Check(AutomationProperties.GetName(Number(c, "LEFT")).Contains('?'), "未知硬件被捏造编号");
            Check(Screen(c, "LEFT").ToolTip!.ToString()!.Contains("未连接"), "未知硬件被当作在线");
        });

        Test("零尺寸、超小、空布局、无效尺寸、极端坐标均不崩溃", () =>
        {
            foreach (var (w, h) in new[] { (0, 0), (1, 1), (20, 12), (100, 60), (400, 300) })
            foreach (var p in new[] {
                Fixture(), new DisplayProfile("empty", "空", []),
                new DisplayProfile("bad", "坏尺寸", [new("BAD", true, true, 0, 0, 0, -1, 0, 60)]),
                new DisplayProfile("far", "远距", [new("FAR", true, true, int.MaxValue, int.MinValue, 32768, 32768, 0, 60)]) })
            {
                var c = Render(p, w, h);
                foreach (var e in Descendants(c).OfType<FrameworkElement>())
                    Check(double.IsFinite(e.ActualWidth) && double.IsFinite(e.ActualHeight), "生成无效几何");
            }
            var pending = new Canvas(); Drawing(pending, Fixture(), Live(Fixture()), Labels, Numbers, null, _ => { });
            var explicitSize = new Canvas { Width = 400, Height = 300 };
            Drawing(explicitSize, Fixture(), Live(Fixture()), Labels, Numbers, null, _ => { }); Layout(explicitSize);
            Check(Number(explicitSize, "MAIN").Source.Width > 0, "首次排版前显式尺寸未生效");
        });

        Test("离屏 PNG 渲染用于组件外观复核，无桌面窗口", () =>
        {
            foreach (var (w, h) in new[] { (1100, 650), (400, 300) }) Save(Render(Fixture(), w, h), $"studio-{w}x{h}.png");
            var p = Fixture(); p.Displays[0] = p.Displays[0] with { Width = 2160, Height = 3840, Rotation = 90, X = -2160 };
            Save(Render(p, 400, 300), "studio-portrait-400x300.png");
            var compact = Fixture(); compact.Displays[0] = compact.Displays[0] with { Y = 0 };
            Save(Render(compact, 900, 492), "studio-compact-106px.png");
            Check(Application.Current == null, "离屏渲染创建了 Application");
        });
        Console.WriteLine($"RESULT {passed} passed, {failed} failed; fake hardware only; no Application, Window or native backend.");
        return failed == 0 ? 0 : 1;
    }

    private static Canvas MakeCanvas(double w, double h)
    { var c = new Canvas { Width = w, Height = h }; Layout(c); return c; }
    private static Canvas Render(DisplayProfile p, double w, double h)
    { var c = MakeCanvas(w, h); Drawing(c, p, Live(p), Labels, Numbers, "left", _ => { }); Layout(c); return c; }
    private static void Layout(Canvas c)
    { c.Measure(new Size(c.Width, c.Height)); c.Arrange(new Rect(0, 0, c.Width, c.Height)); c.UpdateLayout(); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var d in Descendants(child)) yield return d; }
    }
    private static FrameworkElement Element(Canvas c, string id) => Descendants(c).OfType<FrameworkElement>()
        .SingleOrDefault(e => AutomationProperties.GetAutomationId(e) == id) ?? throw new Exception("缺少可见对象 " + id);
    private static Button Screen(Canvas c, string id) => Descendants(c).OfType<Button>().Single(b => Equals(b.Tag, id));
    private static Image Number(Canvas c, string id) => Element(c, "Studio.Number:" + id) as Image
        ?? throw new Exception("编号应使用图片控件");
    private static FrameworkElement Primary(Canvas c, string id) => Element(c, "Studio.Primary:" + id);
    private static Rect Bounds(Canvas c, FrameworkElement e) => e.TransformToAncestor(c).TransformBounds(new Rect(e.RenderSize));
    private static void Inside(Canvas c, FrameworkElement e) => Contains(new Rect(0, 0, c.Width, c.Height), Bounds(c, e));
    private static void Contains(Rect outer, Rect inner)
    { outer.Inflate(.1, .1); Check(!inner.IsEmpty && outer.Contains(inner), $"元素超出容器：{inner} / {outer}"); }
    private static void Near(double a, double b, double epsilon = .001) => Check(Math.Abs(a - b) < epsilon, $"几何不符：{a} / {b}");
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static string SourceDirectory([CallerFilePath] string file = "") => System.IO.Path.GetDirectoryName(file)!;
    private static void Save(Canvas c, string name)
    {
        var bitmap = new RenderTargetBitmap((int)c.Width, (int)c.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(c);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var folder = System.IO.Path.Combine(SourceDirectory(), "artifacts"); Directory.CreateDirectory(folder);
        using var file = File.Create(System.IO.Path.Combine(folder, name)); encoder.Save(file);
    }
}
