using System.Drawing;
using System.Windows;

namespace PingXu.App;

public static class BrandAssets
{
    public const string IconUri = "pack://application:,,,/PingXu;component/Assets/PingXu.ico";
    public static Icon CreateTrayIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri(IconUri)) ?? throw new InvalidOperationException("应用图标资源缺失。");
        using var stream = resource.Stream;
        using var icon = new Icon(stream, new System.Drawing.Size(32, 32));
        return (Icon)icon.Clone();
    }
    public static string Version => typeof(BrandAssets).Assembly.GetName().Version?.ToString(3) ?? "开发版";
}
