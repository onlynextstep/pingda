using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Image = System.Windows.Controls.Image;
using FlowDirection = System.Windows.FlowDirection;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace PingXu.App;

/// <summary>User-supplied, background-extracted artwork. No glyph reconstruction or runtime file dependency.</summary>
public static class NumberArtwork
{
    private static readonly ConcurrentDictionary<string, ImageSource> Sources = new(StringComparer.Ordinal);

    public static Image Create(string number)
    {
        var image = new Image { Source = Sources.GetOrAdd(number, Load), Stretch = Stretch.Uniform,
            IsHitTestVisible = false, SnapsToDevicePixels = false };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    private static ImageSource Load(string number)
    {
        if (int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value is >= 1 and <= 6)
        {
            using var stream = typeof(NumberArtwork).Assembly.GetManifestResourceStream($"PingXu.Numbers.{value:00}.png")
                ?? throw new InvalidOperationException($"屏幕编号 {value:00} 的图片资源缺失，请重新安装屏搭。");
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            frame.Freeze(); return frame;
        }
        // Old/offline identities and unsupported hardware stay readable; never assign a different screen's image.
        var text = new FormattedText(number, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas"), 32, Brushes.LightGray, 1);
        var group = new DrawingGroup();
        using (var context = group.Open()) context.DrawText(text, new Point(0, 0));
        var fallback = new DrawingImage(group); fallback.Freeze(); return fallback;
    }
}
