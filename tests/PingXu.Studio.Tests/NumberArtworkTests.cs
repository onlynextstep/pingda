using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PingXu.App;
using PingXu.Core;

namespace PingXu.Studio.Tests;

internal static class NumberArtworkTests
{
    public static void Register(Action<string, Action> test)
    {
        test("六张打包素材逐像素对应存档原图，不重绘、不缺点、不增加点阵", () =>
        {
            // Independently recorded extraction bounds; changing glyph geometry or using generated alternatives must fail.
            var crops = new[] { new Int32Rect(282,313,644,580), new Int32Rect(254,331,746,559),
                new Int32Rect(266,334,722,560), new Int32Rect(253,340,771,559), new Int32Rect(229,303,792,620), new Int32Rect(249,339,758,573) };
            for (int i = 0; i < 6; i++)
            {
                string path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "../../design/number-sources", $"{i + 1:00}.png"));
                using var stream = File.OpenRead(path);
                var original = new FormatConvertedBitmap(BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad), PixelFormats.Bgra32, null, 0);
                var crop = crops[i]; var sourcePixels = new byte[crop.Width * crop.Height * 4];
                original.CopyPixels(crop, sourcePixels, crop.Width * 4, 0);
                var asset = new FormatConvertedBitmap((BitmapSource)NumberArtwork.Create($"{i + 1:00}").Source, PixelFormats.Bgra32, null, 0);
                Check(asset.PixelWidth == crop.Width && asset.PixelHeight == crop.Height, "原始像素尺寸发生缩放或重排");
                var outputPixels = new byte[sourcePixels.Length]; asset.CopyPixels(outputPixels, crop.Width * 4, 0);
                for (int p = 0; p < sourcePixels.Length; p += 4)
                {
                    int light = Math.Max(sourcePixels[p], Math.Max(sourcePixels[p + 1], sourcePixels[p + 2]));
                    int expectedAlpha = Math.Max(0, (light - 36) * 255 / 219);
                    Check(outputPixels[p + 3] == expectedAlpha, $"编号{i + 1:00}像素{p / 4}不再对应原图白点/光晕");
                    if (expectedAlpha != 0) Check(outputPixels[p] == 255 && outputPixels[p + 1] == 255 && outputPixels[p + 2] == 255, "光晕带入黑边或色偏");
                }
            }
        });
        test("01至06使用六张独立透明位图，白点和半透明微光均存在", () =>
        {
            var signatures = new HashSet<string>();
            var canvas = Render(6, 1600, 900);
            for (int i = 1; i <= 6; i++)
            {
                var image = Number(canvas, i);
                Check(image.Source is BitmapSource, "屏幕编号仍由旧字模绘制，未使用用户位图");
                var source = new FormatConvertedBitmap((BitmapSource)image.Source, PixelFormats.Bgra32, null, 0);
                Check(source.PixelHeight >= 400 && source.PixelWidth >= 400, "打包的数字只有缩略图清晰度");
                var pixels = new byte[source.PixelWidth * source.PixelHeight * 4]; source.CopyPixels(pixels, source.PixelWidth * 4, 0);
                int clear = 0, glow = 0, white = 0;
                for (int n = 0; n < pixels.Length; n += 4)
                {
                    byte alpha = pixels[n + 3];
                    if (alpha == 0) clear++;
                    if (alpha is > 0 and < 160) glow++;
                    if (alpha > 230 && pixels[n] > 230) white++;
                }
                Check(clear > pixels.Length / 16, "黑背景没有真正变透明");
                Check(glow > 500 && white > 500, "白点或微光被硬阈值抹掉");
                Check(pixels[3] == 0 && pixels[^1] == 0, "素材边角留有背景杂点");
                signatures.Add(Convert.ToHexString(SHA256.HashData(pixels)));
                Check(AutomationProperties.GetName(image).EndsWith(i.ToString("00")), "可访问编号与屏幕身份不一致");
                Check(!image.IsHitTestVisible, "编号图片会拦截屏幕拖动");
            }
            Check(signatures.Count == 6, "重复使用了同一个编号图片");
        });
        test("1至6屏横竖混排在小窗口及4K画布中编号不拉伸、不越界且不拦截选择", () =>
        {
            foreach (int count in Enumerable.Range(1, 6))
            foreach (var size in new[] { new Size(400, 300), new Size(1100, 650), new Size(3440, 1440), new Size(3840, 2160) })
            {
                var canvas = Render(count, size.Width, size.Height);
                for (int i = 1; i <= count; i++)
                {
                    var image = Number(canvas, i);
                    var screen = Descendants(canvas).OfType<Button>().Single(b => Equals(b.Tag, "D" + i));
                    Check(image.Source != null && image.Stretch == Stretch.Uniform, "编号缺失或被拉伸");
                    var rect = image.TransformToAncestor(screen).TransformBounds(new Rect(image.RenderSize));
                    var frame = new Rect(screen.RenderSize); frame.Inflate(.1, .1);
                    Check(frame.Contains(rect) && rect.Height > 0, "编号被裁切或消失");
                }
            }
        });
        test("六屏混排离屏截图用于逐张对照，不连接真实显示服务", () =>
        {
            var canvas = Render(6, 1600, 900);
            var bitmap = new RenderTargetBitmap(1600, 900, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var directory = Path.Combine(Path.GetDirectoryName(SourcePath())!, "artifacts"); Directory.CreateDirectory(directory);
            using var output = File.Create(Path.Combine(directory, "six-screen-artwork.png")); encoder.Save(output);
            Check(Application.Current == null, "测试不应创建应用或桌面窗口");
        });
    }

    static Canvas Render(int count, double width, double height)
    {
        var targets = Enumerable.Range(1, count).Select(i => new DisplayTarget("D" + i, true, i == 1,
            (i - 1) % 3 * 3840, (i - 1) / 3 * 2400, i % 2 == 0 ? 1080 : 3840, i % 2 == 0 ? 1920 : 2160,
            i % 2 == 0 ? 90 : 0, 60)).ToList();
        var canvas = new Canvas { Width = width, Height = height };
        canvas.Measure(new Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height));
        StudioDrawing.DrawLayout(canvas, new("six", "六屏测试", targets), [], new Dictionary<string, string>(),
            Enumerable.Range(1, count).ToDictionary(i => "D" + i, i => i), "D1", _ => { });
        canvas.Measure(new Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height)); canvas.UpdateLayout();
        return canvas;
    }
    static Image Number(Canvas canvas, int number) => Descendants(canvas).OfType<FrameworkElement>()
        .Single(e => AutomationProperties.GetAutomationId(e) == "Studio.Number:D" + number) as Image
        ?? throw new Exception("屏幕编号尚未替换为图片");
    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var d in Descendants(child)) yield return d; }
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
