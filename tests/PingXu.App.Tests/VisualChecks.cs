using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PingXu.App.Tests;

internal static class VisualChecks
{
    public static Color Solid(Brush brush) => brush is SolidColorBrush { Color.A: 255 } solid
        ? solid.Color : throw new Exception("颜色须为不透明纯色，不能跳过文字对比检查");

    private static double Luminance(Color color)
    {
        static double Linear(byte value)
        {
            double s = value / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }

    private static double Contrast(Color a, Color b)
    {
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }

    public static string Background(WindowFixture f)
    {
        var actual = Solid(f.Window.Background);
        var expected = Solid((Brush)Application.Current.FindResource("Bg"));
        if (f.Window.Style == null || actual != expected || Luminance(actual) >= .25)
            throw new Exception($"窗口深色主题未应用：Style={f.Window.Style}, Background={actual}, ThemeBg={expected}");
        double contrast = Contrast(Solid(f.Window.Foreground), actual);
        if (contrast < 4.5) throw new Exception($"窗口文字对比不足：{contrast:F2}:1");
        return $"窗口底色 {actual}，窗口文字对比 {contrast:F2}:1";
    }

    public static string Primary(WindowFixture f)
    {
        // The workspace Apply action intentionally does not exist visually while viewing the live layout.
        // Inspect a rendered primary action, not a collapsed button's absent visual template.
        var primary = (Style)f.Window.FindResource("Primary");
        var button = Descendants(f.Content).OfType<Button>().First(b => b.Style == primary && b.Visibility == Visibility.Visible);
        var text = Descendants(button).OfType<TextBlock>().SingleOrDefault(t => t.Text == button.Content?.ToString())
            ?? throw new Exception("未找到主按钮模板生成的实际 TextBlock，不能只验证 Button.Foreground");
        var textColor = Solid(text.Foreground);
        var buttonColor = Solid(button.Foreground);
        var source = DependencyPropertyHelper.GetValueSource(text, TextBlock.ForegroundProperty);
        if (textColor != buttonColor || source.BaseValueSource != BaseValueSource.Inherited)
            throw new Exception($"主按钮文字未继承：TextBlock={textColor}, Button={buttonColor}, 来源={source.BaseValueSource}");
        // Also observe the template's rendered chrome; checking the property alone misses a broken TemplateBinding.
        var chrome = button.Template.FindName("Chrome", button) as Border
            ?? throw new Exception("未找到主按钮模板 Chrome");
        var background = Solid(chrome.Background);
        if (background != Solid(button.Background)) throw new Exception("主按钮模板未使用按钮背景");
        double contrast = Contrast(textColor, background);
        if (contrast < 4.5) throw new Exception($"主按钮实际文字与背景对比不足：{contrast:F2}:1");
        return $"Primary实际文字 {textColor} / 背景 {background}，来源 {source.BaseValueSource}，对比 {contrast:F2}:1";
    }

    public static void Layout(WindowFixture f, int width = 1400, int height = 940)
    {
        f.Content.Measure(new Size(width, height));
        f.Content.Arrange(new Rect(0, 0, width, height));
        f.Content.UpdateLayout();
    }

    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
