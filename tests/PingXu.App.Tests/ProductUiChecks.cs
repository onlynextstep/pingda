using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PingXu.Core;

namespace PingXu.App.Tests;

internal static class ProductUiChecks
{
    public static void Register(Action<string, Action> test, WindowFixture f)
    {
        test("屏搭品牌名称显示在窗口与标题区", () =>
        {
            VisualChecks.Layout(f, 1488, 1020);
            Check(f.Window.Title == "屏搭 · 多屏工作空间", "窗口仍显示旧品牌");
            Check(VisualChecks.Descendants(f.Content).OfType<TextBlock>().Any(t => t.Text == "屏搭"), "标题区未使用新品牌");
        });
        test("工作空间入口使用四个等大的矢量方框，不依赖系统字体图标", () =>
        {
            VisualChecks.Layout(f, 1488, 1020);
            var button = VisualChecks.Descendants(f.Content).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "工作空间");
            Check(button.Content is Viewbox, "工作空间仍使用系统字体图标");
            var boxes = VisualChecks.Descendants(button).OfType<System.Windows.Shapes.Rectangle>().ToArray();
            Check(boxes.Length == 4 && boxes.All(b => b.Width == b.Height && b.Width > 10 && b.RadiusX > 0), "没有四个等大的圆角方框");
        });
        test("标题栏鼠标焦点不留下黄框且保留键盘焦点样式", () =>
        {
            var button = new Button { Style = (Style)f.Window.FindResource("Caption") };
            button.Measure(new Size(42, 42)); button.Arrange(new Rect(0, 0, 42, 42)); button.ApplyTemplate();
            var key = (DependencyPropertyKey)typeof(UIElement).GetField("IsKeyboardFocusedPropertyKey",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
            button.SetValue(key, true);
            var chrome = (Border)button.Template.FindName("Chrome", button);
            Check(chrome.BorderBrush is SolidColorBrush ink && ink.Color.A == 0,
                "仅获得焦点就残留标题栏黄框");
            Check(button.Focusable && button.FocusVisualStyle != null, "不能通过禁用键盘焦点掩盖问题");
            button.SetValue(key, false);
        });
        test("六块已连接屏幕优先分配01至06，历史记录不挤占图片编号", () =>
        {
            var template = FakeDisplayService.Snapshot().Displays[0];
            var live = Enumerable.Range(1, 6).Select(i => template with { Id = "Z" + i, Connected = true }).ToList();
            var historical = template with { Id = "A-old", Connected = false, Enabled = false, Primary = false };
            var numbers = DisplayPresentation.Numbers(live.Prepend(historical), [Target(historical with { Id = "B-old" })]);
            foreach (int i in Enumerable.Range(1, 6)) Check(numbers["Z" + i] == i, "历史设备改变了已连接屏幕编号");
            Check(numbers.Count == 8 && numbers["A-old"] > 6 && numbers["B-old"] > 6, "历史记录被删除或冒充在线屏幕");
        });
        test("七屏只读显示并明确提示上限，回到六屏后恢复编辑入口", () =>
        {
            var actual = FakeDisplayService.Snapshot(); var template = actual.Displays[0];
            var hardware = Enumerable.Range(1, 7).Select(i => template with { Id = "Z" + i,
                DeviceName = "FAKE" + i, Connected = true, Enabled = true, Primary = i == 1, X = (i - 1) * 3840, Y = 0 }).ToList();
            f.Set("snapshot", actual with { Displays = hardware });
            f.Set("draft", new DisplayProfile("seven", "七屏", hardware.Select(Target).ToList()));
            f.Set("selected", "Z1"); f.Set("dirty", true);
            f.Invoke("RenderCanvas"); f.Invoke("PopulateInspector"); VisualChecks.Layout(f);
            Check(!f.Get<Button>("ApplyButton").IsEnabled && !f.Get<StackPanel>("Inspector").IsEnabled
                && !f.Get<Button>("NewPresetButton").IsEnabled, "超过六块时仍可更改显示布局");
            Check(f.Get<TextBlock>("Subtitle").Text.Contains("6") && f.Get<TextBlock>("Subtitle").Text.Contains("7"), "未明确解释当前数量与软件上限");
            Check(VisualChecks.Descendants(f.Get<Canvas>("LayoutCanvas")).OfType<Button>().Count(b => b.Tag is string) == 7,
                "过限后偷偷截断了可见屏幕");
            f.Set("snapshot", actual with { Displays = hardware.Take(6).ToList() });
            f.Set("draft", new DisplayProfile("six", "六屏", hardware.Take(6).Select(Target).ToList()));
            f.Invoke("RenderCanvas"); f.Invoke("PopulateInspector");
            Check(f.Get<Button>("ApplyButton").IsEnabled && f.Get<StackPanel>("Inspector").IsEnabled, "恢复六屏后仍被锁定");
        });
        test("识别屏幕浮层复用原图编号，长名称完整换行且主屏明确", () =>
        {
            var factory = typeof(DesktopTools).GetMethod("CreateIdentificationContent")
                ?? throw new Exception("识别屏幕仍使用旧文本，没有共享图片内容组件");
            foreach (int i in Enumerable.Range(1, 6))
            {
                var name = "这是一个用于验证完整显示的长屏幕名称 · 测试型号";
                var content = (FrameworkElement)factory.Invoke(null, [i, name, true])!;
                content.Measure(new Size(320, double.PositiveInfinity)); content.Arrange(new Rect(content.DesiredSize));
                var image = VisualChecks.Descendants(content).OfType<Image>().Single();
                Check(ReferenceEquals(image.Source, NumberArtwork.Create(i.ToString("00")).Source), "识别浮层与工作空间没有共享图片");
                var labels = VisualChecks.Descendants(content).OfType<TextBlock>().ToList();
                Check(labels.Any(t => t.Text == name && t.TextWrapping == TextWrapping.Wrap) && labels.Any(t => t.Text == "主屏"), "名称或主屏身份丢失");
            }
        });
        test("主界面短文案清晰区分当前布局、未应用修改与保存预设", () =>
        {
            Check(f.Get<TextBlock>("PreviewTag").Text == "当前布局", "默认状态文案不正确");
            Check(Equals(f.Get<CheckBox>("EnabledToggle").Content, "启用此屏幕"), "启用开关文案不清晰");
            Check(Equals(f.Get<Expander>("AdvancedExpander").Header, "位置与对齐"), "位置入口文案不清晰");
            Check(f.Get<Button>("SaveAsButton").ToolTip.ToString()!.Contains("不立即切换"), "保存与应用未区分");
            Check(f.Get<TextBlock>("TopologyLabel").Text.EndsWith("块屏幕"), "数量说明未使用中文");
            var previousNotice = f.Get<TextBlock>("StatusLabel").Text;
            f.Get<ComboBox>("RotationBox").SelectedIndex = 1;
            Check(f.Get<TextBlock>("PreviewTag").Text == "有未应用的修改", "编辑状态误报生效");
            Check(f.Get<TextBlock>("StatusLabel").Text == previousNotice, "普通编辑不应遮盖画布或覆盖已有待处理提示");
        });
        test("定稿品牌资源保持正方形切角底板、横竖实心屏与共用底座，PNG和ICO一致", () =>
        {
            using var stream = Application.GetResourceStream(new Uri(BrandAssets.IconUri))!.Stream;
            var frames = System.Windows.Media.Imaging.BitmapDecoder.Create(stream,
                System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames;
            foreach (var frame in frames)
            {
                var rgba = new System.Windows.Media.Imaging.FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                var pixels = new byte[rgba.PixelWidth * rgba.PixelHeight * 4];
                rgba.CopyPixels(pixels, rgba.PixelWidth * 4, 0);
                if (frame.PixelWidth >= 32)
                {
                    int cut = ((int)(.12 * rgba.PixelHeight) * rgba.PixelWidth + (int)(.92 * rgba.PixelWidth)) * 4;
                    Check(pixels[cut + 3] < 20, "右上切角未透明镂空");
                }
                // Interior samples avoid antialiasing edges; catches stale or independently redrawn frames.
                foreach (var (x,y,yellow) in new[] { (.5,.14,true), (.24,.4,false), (.65,.52,false), (.5,.73,false), (.5,.9,true) })
                {
                    int at = ((int)(y * rgba.PixelHeight) * rgba.PixelWidth + (int)(x * rgba.PixelWidth)) * 4;
                    // At 16px the thin base covers part of a pixel; classify foreground/background, not exact ink.
                    Check(yellow ? pixels[at + 1] > 220 && pixels[at + 2] > 180 : pixels[at + 1] < 128 && pixels[at + 2] < 128,
                        $"{frame.PixelWidth}px品牌图形与定稿不一致 ({x},{y})");
                }
            }
            using var pngStream = Application.GetResourceStream(new Uri("pack://application:,,,/PingXu;component/Assets/pingxu-icon.png"))!.Stream;
            var png = System.Windows.Media.Imaging.BitmapDecoder.Create(pngStream, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
            byte[] Read(System.Windows.Media.Imaging.BitmapSource source)
            {
                var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                var bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4];
                converted.CopyPixels(bytes, converted.PixelWidth * 4, 0); return bytes;
            }
            Check(Read(png).SequenceEqual(Read(frames.Single(frame => frame.PixelWidth == 256))), "PNG和ICO不是同一母版导出");
        });
        test("品牌图标嵌入窗口与托盘，包含多尺寸帧且无半透明杂点", () =>
        {
            Check(f.Window.Icon != null && Version.TryParse(BrandAssets.Version, out var brandVersion) && brandVersion.Build >= 0, "窗口图标或程序集版本缺失");
            var resource = Application.GetResourceStream(new Uri(BrandAssets.IconUri))!;
            using var stream = resource.Stream;
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream,
                System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            Check(decoder.Frames.Select(b => b.PixelWidth).SequenceEqual(new[] { 16,20,24,32,40,48,64,128,256 }), "ICO 缺少尺寸帧");
            foreach (var frame in decoder.Frames)
            {
                var rgba = new System.Windows.Media.Imaging.FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                var pixels = new byte[rgba.PixelWidth * rgba.PixelHeight * 4];
                rgba.CopyPixels(pixels, rgba.PixelWidth * 4, 0);
                Check(pixels[3] == 0 && pixels[(rgba.PixelWidth - 1) * 4 + 3] == 0, "图标外围和切角必须透明，不能留下黑底");
                int center = ((rgba.PixelHeight / 2) * rgba.PixelWidth + rgba.PixelWidth / 2) * 4;
                Check(pixels[center + 3] == 255, "内部图形不应变透明");
            }
            using var tray = BrandAssets.CreateTrayIcon();
            Check(tray.Width == 32 && tray.Height == 32, "托盘资源不能作为32px图标解码");
        });
        test("通用名称：同型号去歧义、不生成位置/4K名称、不写入别名", () =>
        {
            var displays = FakeDisplayService.Snapshot().Displays.Select((d, i) => d with { Name = i < 2 ? "DELL U2723QE" : "" }).ToList();
            var aliases = new Dictionary<string, string>();
            var numbers = DisplayPresentation.Numbers(displays);
            var labels = DisplayPresentation.Labels(displays, aliases, numbers);
            Check(aliases.Count == 0 && labels.Values.Distinct().Count() == 3, "默认名称持久化或同型号无法区分");
            Check(labels.Values.Count(n => n.StartsWith("DELL U2723QE · ")) == 2 && labels.Values.Any(n => n.StartsWith("显示器 ")), "型号或无名称回退错误");
            Check(labels.Values.All(n => !n.Contains("4K") && !n.Contains("带鱼")), "生成私人名称");
            aliases[displays[0].Id.ToLowerInvariant()] = "我的阅读屏";
            Check(DisplayPresentation.Labels(displays, aliases, numbers)[displays[0].Id] == "我的阅读屏", "自定义名称未按稳定身份保留");
        });
        test("编号与默认名称不受别名顺序、坐标旋转和后台枚举次序影响", () =>
        {
            var original = FakeDisplayService.Snapshot().Displays;
            var a = DisplayPresentation.Numbers(original);
            var b = DisplayPresentation.Numbers(original.AsEnumerable().Reverse().Select(d => d with { X = d.X + 100, Rotation = 90, Enabled = false }));
            Check(a.All(p => b[p.Key] == p.Value), "编号随几何变化漂移");
            Check(DisplayPresentation.Labels(original, new Dictionary<string, string>(), a).Count == original.Count, "未提供默认系统标签");
        });
        test("首次通用场景入口保留实测布局且单屏名称来自实际型号", () =>
        {
            var displays = FakeDisplayService.Snapshot().Displays.Select((d, i) => d with { Name = "测试型号" + i }).ToList();
            f.Get<Dictionary<string, string>>("aliases").Clear();
            f.Invoke("GenerateProfiles", displays);
            Check(f.Profiles.Count == displays.Count + 1, "仍强制生成五种私人预设");
            Check(DisplayPresentation.SameLayout(f.Profiles[0].Displays, displays.Select(Target)), "首次未保存精确实际布局");
            Check(f.Profiles.Skip(1).All(p => p.Name.Contains("测试型号") && p.Displays.Count(d => d.Enabled) == 1), "单屏名称或数量不是设备驱动");
        });
        test("当前场景全荧光黄；显式编辑其他预设不冒充当前场景", () =>
        {
            var actual = FakeDisplayService.Snapshot();
            var current = new DisplayProfile("actual", "实测场景", actual.Displays.Select(Target).ToList());
            var preview = f.Profiles.Single(p => p.Name == "三屏阅读");
            f.Set("profiles", new List<DisplayProfile> { current, preview });
            f.Set("editingPresetId", preview.Id);
            f.Invoke("SelectProfile", preview);
            VisualChecks.Layout(f);
            var cards = f.Get<StackPanel>("SceneStrip").Children.OfType<Button>().ToArray();
            Check(IsAcid(cards[0].Background) && !IsAcid(cards[1].Background), "预览取代了当前实测高亮");
            Check(IsAcid(cards[1].BorderBrush), "预览没有独立边框提示");
            Check(System.Windows.Automation.AutomationProperties.GetName(cards[0]).Contains("当前")
                && System.Windows.Automation.AutomationProperties.GetName(cards[1]).Contains("编辑中"), "可访问状态不区分当前/编辑");
            var ink = VisualChecks.Descendants(cards[0]).OfType<TextBlock>().FirstOrDefault(t => t.Text == "实测场景");
            Check(ink?.Foreground is SolidColorBrush { Color: var color } && color == Colors.Black, "当前场景文字不是深色");
        });
        test("当前布局匹配大小写不敏感但不得忽略位置或刷新率", () =>
        {
            var original = FakeDisplayService.Snapshot().Displays.Select(Target).ToList();
            var normalized = original.Select(t => t with { Id = t.Id.ToLowerInvariant() }).Reverse().ToList();
            Check(DisplayPresentation.SameLayout(original, normalized), "仅身份大小写不同导致当前场景丢失");
            normalized[0] = normalized[0] with { Y = normalized[0].Y + 4 };
            Check(!DisplayPresentation.SameLayout(original, normalized), "误把坐标不同的场景显示为当前");
            normalized = original.Select(t => t with { RefreshRate = t.RefreshRate + 1 }).ToList();
            Check(!DisplayPresentation.SameLayout(original, normalized), "忽略实际刷新率差异");
        });
        test("空场景库不自动补充，恢复系统名称入口存在", () =>
        {
            f.Set("profiles", new List<DisplayProfile>()); f.Invoke("RenderScenes");
            VisualChecks.Layout(f);
            Check(f.Get<StackPanel>("SceneStrip").Children.Count == 0, "空场景库被自动再生成");
            Check(VisualChecks.Descendants(f.Content).OfType<Button>().Any(b => Equals(b.Content, "恢复系统名称")), "没有恢复系统名称入口");
        });
    }
    static bool IsAcid(Brush brush) => brush is SolidColorBrush b && b.Color == Color.FromRgb(220, 255, 66);
    static DisplayTarget Target(DisplayInfo d) => new(d.Id, d.Enabled, d.Primary, d.X, d.Y, d.Width, d.Height, d.Rotation, d.RefreshRate);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
