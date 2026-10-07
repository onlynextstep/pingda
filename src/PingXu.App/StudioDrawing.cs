using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using PingXu.Core;
using Binding = System.Windows.Data.Binding;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Canvas = System.Windows.Controls.Canvas;
using Color = System.Windows.Media.Color;
using Control = System.Windows.Controls.Control;
using Cursors = System.Windows.Input.Cursors;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Line = System.Windows.Shapes.Line;
using Orientation = System.Windows.Controls.Orientation;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;

namespace PingXu.App;

/// <summary>Coordinates and hit rectangles are relative to the outer canvas, in DIPs.</summary>
public sealed record StudioDragMapping(double Scale, double OriginX, double OriginY, double SurfaceScale,
    IReadOnlyList<StudioDragVisual> Displays);

/// <summary>One screen plus its external title/selection marks; elements share the drawing surface.</summary>
public sealed record StudioDragVisual(string Id, Rect Bounds, IReadOnlyList<FrameworkElement> Elements);

/// <summary>Pure, offscreen-capable layout preview. Call on the Canvas dispatcher after size/draft changes.</summary>
public static class StudioDrawing
{
    private static readonly ConditionalWeakTable<Canvas, StudioDragMapping> DragMappings = new();
    internal static readonly RoutedEvent LayoutRedrawingEvent = EventManager.RegisterRoutedEvent(
        "LayoutRedrawing", RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(StudioDrawing));

    /// <summary>The exact last drawing transform, including tiny-canvas shrink; null without active screens.</summary>
    public static StudioDragMapping? GetDragMapping(Canvas canvas) =>
        DragMappings.TryGetValue(canvas, out var mapping) ? mapping : null;

    private static readonly Brush Ground = Ink(0x15, 0x16, 0x16);
    private static readonly Brush ScreenFill = Ink(0x1A, 0x1B, 0x1B);
    private static readonly Brush MinorGrid = Ink(0x20, 0x22, 0x22);
    private static readonly Brush MajorGrid = Ink(0x2A, 0x2C, 0x2C);
    private static readonly Brush Rule = Ink(0x4A, 0x4D, 0x4D);
    private static readonly Brush Edge = Ink(0xA2, 0xA5, 0xA3);
    private static readonly Brush Muted = Ink(0x95, 0x99, 0x97);
    private static readonly Brush TextInk = Ink(0xDF, 0xE1, 0xDE);
    private static readonly Brush Acid = Ink(0xDC, 0xFF, 0x42);
    private static readonly FontFamily UiFont = new("Microsoft YaHei UI, Segoe UI");
    private static readonly FontFamily NumericFont = new("Consolas, Microsoft YaHei UI");

    /// <summary>
    /// Replaces the canvas drawing using draft desktop pixel coordinates (already rotated by the caller).
    /// Selection invokes only <paramref name="select"/>; no hardware, persistence or resize handlers are used.
    /// </summary>
    public static void DrawLayout(Canvas canvas, DisplayProfile draft, IReadOnlyList<DisplayInfo> live,
        IReadOnlyDictionary<string, string> labels, IReadOnlyDictionary<string, int> numbers,
        string? selected, Action<string> select)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(numbers);
        ArgumentNullException.ThrowIfNull(select);
        canvas.VerifyAccess();
        canvas.RaiseEvent(new RoutedEventArgs(LayoutRedrawingEvent));
        DragMappings.Remove(canvas);
        canvas.Children.Clear();
        canvas.Background = Ground;
        canvas.ClipToBounds = true;

        double width = Extent(canvas.ActualWidth, canvas.Width);
        double height = Extent(canvas.ActualHeight, canvas.Height);
        if (width <= 0 || height <= 0) return;

        // Uniformly shrink the entire drafting surface for truly tiny viewports, including its annotations.
        // At the supported 400×300 and above, coordinates are direct, unrounded device-independent pixels.
        double shrink = Math.Min(1, Math.Min(width / 400, height / 300));
        var surface = canvas;
        if (shrink < 1)
        {
            width /= shrink;
            height /= shrink;
            surface = new Canvas { Width = width, Height = height, RenderTransform = new ScaleTransform(shrink, shrink) };
            Place(canvas, surface, 0, 0);
        }

        var active = draft.Displays.Where(d => d.Enabled && d.Width > 0 && d.Height > 0).ToList();
        var inactive = draft.Displays.Where(d => !d.Enabled || d.Width <= 0 || d.Height <= 0).ToList();
        double minX = active.Count == 0 ? 0 : active.Min(d => (double)d.X);
        double minY = active.Count == 0 ? 0 : active.Min(d => (double)d.Y);
        // Promote BEFORE addition, so even an unvalidated int-boundary draft is safe to preview.
        double maxX = active.Count == 0 ? 0 : active.Max(d => (double)d.X + d.Width);
        double maxY = active.Count == 0 ? 0 : active.Max(d => (double)d.Y + d.Height);
        double rulerWidth = Math.Max(60, Math.Max(Pixel(minY).Length, Pixel(maxY).Length) * 6 + 16);
        var plot = new Rect(rulerWidth, 38, Math.Max(1, width - rulerWidth - 18),
            Math.Max(1, height - 38 - (inactive.Count > 0 ? 70 : 22)));

        var yCaption = Label("Y（像素）", 10, Muted);
        Place(surface, yCaption, 3, 1);
        var xCaption = Label("X（像素）", 10, Muted);
        Place(surface, xCaption, width - 77, 1);

        if (active.Count > 0)
        {
            const double padding = 16;
            double nameScale = Math.Clamp(width / 1100, 1, 2.4);
            double nameSpace = 28 * nameScale;
            double scale = Math.Min((plot.Width - 2 * padding) / Math.Max(1, maxX - minX),
                (plot.Height - nameSpace - 2 * padding) / Math.Max(1, maxY - minY));
            double originX = plot.Left + (plot.Width - (maxX - minX) * scale) / 2 - minX * scale;
            double originY = plot.Top + nameSpace + (plot.Height - nameSpace - (maxY - minY) * scale) / 2 - minY * scale;
            DrawGrid(surface, plot, originX, originY, scale);

            // Selected screens sit above overlapping drafts; their true rectangle is never inflated to fit text.
            var dragVisuals = new List<StudioDragVisual>();
            foreach (var target in active.OrderBy(d => Same(d.Id, selected) ? 1 : 0))
            {
                var rect = new Rect(originX + target.X * scale, originY + target.Y * scale,
                    target.Width * scale, target.Height * scale);
                var visual = DrawScreen(surface, rect, target, live, labels, numbers, Same(target.Id, selected), select, nameScale);
                dragVisuals.Add(visual with { Bounds = new Rect(rect.X * shrink, rect.Y * shrink,
                    rect.Width * shrink, rect.Height * shrink) });
            }
            DragMappings.Add(canvas, new StudioDragMapping(scale * shrink, originX * shrink,
                originY * shrink, shrink, dragVisuals.AsReadOnly()));
        }
        else
        {
            DrawAxes(surface, plot);
            var empty = Label(draft.Displays.Count == 0 ? "暂无显示器布局" : "没有可绘制的启用显示器", 13, Muted);
            empty.Width = plot.Width;
            empty.TextAlignment = TextAlignment.Center;
            Place(surface, empty, plot.Left, plot.Top + plot.Height / 2 - 10);
        }

        if (inactive.Count > 0) DrawInactive(surface, plot.Left, height - 48, width - plot.Left - 18,
            inactive, live, labels, numbers, selected, select);
    }

    private static void DrawGrid(Canvas canvas, Rect plot, double originX, double originY, double scale)
    {
        double major = NiceStep(96 / scale), minor = Math.Max(1, major / 10);
        DrawAxisGrid(true, originX, plot.Left, plot.Right);
        DrawAxisGrid(false, originY, plot.Top, plot.Bottom);
        DrawAxes(canvas, plot);

        void DrawAxisGrid(bool horizontal, double origin, double start, double end)
        {
            double first = Math.Ceiling((start - origin) / scale / minor);
            int count = (int)Math.Ceiling((end - start) / scale / minor) + 1;
            for (int i = 0; i <= count; i++)
            {
                double index = first + i, value = index * minor, position = origin + value * scale;
                if (position < start - .01 || position > end + .01) continue;
                bool majorTick = Math.Abs(index % (major / minor)) < .01;
                bool zero = Math.Abs(value) < .001;
                var line = horizontal
                    ? Segment(position, plot.Top, position, plot.Bottom, zero ? Rule : majorTick ? MajorGrid : MinorGrid, zero ? 1 : .6)
                    : Segment(plot.Left, position, plot.Right, position, zero ? Rule : majorTick ? MajorGrid : MinorGrid, zero ? 1 : .6);
                Identify(line, $"Studio.Grid.{(horizontal ? "X" : "Y")}:{Pixel(value)}");
                canvas.Children.Add(line);
                canvas.Children.Add(horizontal
                    ? Segment(position, plot.Top - (majorTick ? 7 : 3), position, plot.Top, Rule, .8)
                    : Segment(plot.Left - (majorTick ? 7 : 3), position, plot.Left, position, Rule, .8));
                if (!majorTick) continue;
                var text = Label(Pixel(value), 10, Muted, true);
                text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                text.Width = text.DesiredSize.Width;
                text.Height = 14;
                double x = horizontal ? position - text.Width / 2 : plot.Left - text.Width - 11;
                double y = horizontal ? plot.Top - 23 : position - text.Height / 2;
                // Omit a boundary tick label only when its full string cannot fit; never truncate a negative sign.
                if (x < 0 || x + text.Width > plot.Right || (!horizontal && (y < plot.Top || y + text.Height > plot.Bottom))) continue;
                Identify(text, $"Studio.Tick.{(horizontal ? "X" : "Y")}:{Pixel(value)}");
                Place(canvas, text, x, y);
            }
        }
    }

    private static void DrawAxes(Canvas canvas, Rect plot)
    {
        canvas.Children.Add(Segment(plot.Left, plot.Top, plot.Right, plot.Top, Rule, 1));
        canvas.Children.Add(Segment(plot.Left, plot.Top, plot.Left, plot.Bottom, Rule, 1));
        canvas.Children.Add(Segment(plot.Left - 4, plot.Top, plot.Left + 5, plot.Top, Edge, 1));
        canvas.Children.Add(Segment(plot.Left, plot.Top - 4, plot.Left, plot.Top + 5, Edge, 1));
    }

    private static StudioDragVisual DrawScreen(Canvas canvas, Rect rect, DisplayTarget target, IReadOnlyList<DisplayInfo> live,
        IReadOnlyDictionary<string, string> labels, IReadOnlyDictionary<string, int> numbers, bool selected, Action<string> select, double nameScale)
    {
        int firstElement = canvas.Children.Count;
        string name = DisplayName(target.Id, live, labels), number = DisplayNumber(target.Id, live, numbers);
        var button = SelectButton(target, name, number, live, selected, select);
        button.Width = rect.Width;
        button.Height = rect.Height;
        button.Content = ScreenAnnotations(target, number, rect.Width, rect.Height);
        Place(canvas, button, rect.Left, rect.Top);

        var title = Label(name, (rect.Width > 220 ? 15 : 12) * nameScale, TextInk);
        title.Width = rect.Width;
        title.Height = 21 * nameScale;
        title.TextAlignment = TextAlignment.Center;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.ToolTip = name;
        title.IsHitTestVisible = true;
        Identify(title, "Studio.Label:" + target.Id, name);
        Place(canvas, title, rect.Left, rect.Top - 26 * nameScale);

        if (selected)
        {
            double size = Math.Min(10 * Math.Sqrt(nameScale), Math.Min(rect.Width, rect.Height) * .14);
            Corner(rect.Right, rect.Top, "NE");
            Corner(rect.Left, rect.Bottom, "SW");
            void Corner(double x, double y, string corner)
            {
                var marker = new Rectangle { Width = size, Height = size, Fill = Acid, IsHitTestVisible = false };
                Identify(marker, $"Studio.Corner:{target.Id}:{corner}");
                Place(canvas, marker, x - size / 2, y - size / 2);
            }
        }
        return new StudioDragVisual(target.Id, rect,
            Array.AsReadOnly(canvas.Children.Cast<FrameworkElement>().Skip(firstElement).ToArray()));
    }

    private static Canvas ScreenAnnotations(DisplayTarget target, string number, double width, double height)
    {
        var content = new Canvas { Width = width, Height = height, IsHitTestVisible = false };
        double padding = Math.Min(10, Math.Min(width, height) * .09);
        double innerWidth = Math.Max(.001, width - padding * 2), innerHeight = Math.Max(.001, height - padding * 2);
        // Use the preview rectangle in DIPs, never the monitor's native resolution or desktop settings.
        // Keep metadata readable while leaving breathing room around the numeral on wide screens.
        double textScale = Math.Clamp(Math.Min(width / 280, height / 120), 1, 4);
        double emphasisScale = Math.Max(1, textScale / 1.5);
        // Reserve the same badge row on every screen; changing Primary must not resize equal-frame numerals.
        double primaryHeight = Math.Min(13 * emphasisScale, innerHeight * .42);
        double primaryGap = Math.Min(6 * emphasisScale, innerHeight * .06);
        double metadataGap = height < 120 ? 4 : 8 * emphasisScale, lineGap = 2 * emphasisScale;
        // Measure actual font ink before assigning line boxes (no fixed 14-DIP clipping at larger sizes).
        var resolution = Label($"{target.Width} × {target.Height}", 12, Muted, true);
        var ratio = Label(AspectRatio(target.Width, target.Height), 11, Muted, true);
        double metadataHeight = 0;
        bool detail;
        do
        {
            resolution.FontSize = 12 * textScale;
            ratio.FontSize = 11 * textScale;
            resolution.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            ratio.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            resolution.Height = Math.Ceiling(resolution.DesiredSize.Height);
            ratio.Height = Math.Ceiling(ratio.DesiredSize.Height);
            metadataHeight = metadataGap + resolution.Height + lineGap + ratio.Height;
            detail = width >= 115 && resolution.DesiredSize.Width <= innerWidth && ratio.DesiredSize.Width <= innerWidth
                && innerHeight - primaryHeight - primaryGap - metadataHeight >= 28;
            if (detail || textScale <= 1) break;
            textScale = Math.Max(1, textScale - .1);
            resolution.Height = ratio.Height = double.NaN;
        } while (true);
        if (!detail) metadataHeight = 0;

        double FitNumber(double aspect) => Math.Max(.001, Math.Min(70 * emphasisScale,
            Math.Min(innerHeight - metadataHeight - primaryHeight - primaryGap, innerWidth * .76 / aspect)));
        var numberPath = NumberArtwork.Create(number);
        double aspect = numberPath.Source.Width / numberPath.Source.Height;
        double numberHeight = FitNumber(aspect);
        numberPath.Width = numberHeight * aspect;
        numberPath.Height = numberHeight;
        Identify(numberPath, "Studio.Number:" + target.Id, "显示器编号 " + number);
        // Anchor the numeral optically near the screen centre. On shallow previews the full
        // annotation stack takes priority; reserve the badge equally to keep peer screens aligned.
        double stackHeight = numberHeight + metadataHeight + primaryGap + primaryHeight;
        double top = Math.Clamp(height * .43 - numberHeight / 2, padding,
            Math.Max(padding, height - padding - stackHeight));
        Place(content, numberPath, (width - numberPath.Width) / 2, top);
        top += numberHeight;

        if (detail)
        {
            top += metadataGap;
            resolution.Width = innerWidth;
            resolution.TextAlignment = TextAlignment.Center;
            Identify(resolution, "Studio.Resolution:" + target.Id);
            Place(content, resolution, padding, top);
            ratio.Width = innerWidth;
            ratio.TextAlignment = TextAlignment.Center;
            Identify(ratio, "Studio.Ratio:" + target.Id);
            Place(content, ratio, padding, top + resolution.Height + lineGap);
            top += resolution.Height + lineGap + ratio.Height;
        }
        if (target.Primary)
        {
            var badge = new Canvas { Width = 43, Height = 13, IsHitTestVisible = false };
            badge.Children.Add(new Rectangle { Width = 10, Height = 7, Stroke = Edge, StrokeThickness = .8, Margin = new Thickness(0, 2, 0, 0) });
            badge.Children.Add(Segment(5, 9, 5, 11, Edge, .8));
            badge.Children.Add(Segment(2, 11, 8, 11, Edge, .8));
            var text = Label("主屏", 9, TextInk);
            Place(badge, text, 16, 0);
            // Scale the complete badge as a unit: neither Chinese glyph can be ellipsized or cut off.
            var view = new Viewbox { Width = Math.Min(43 * emphasisScale, innerWidth), Height = primaryHeight,
                Stretch = Stretch.Uniform, Child = badge, IsHitTestVisible = false };
            Identify(view, "Studio.Primary:" + target.Id, "主屏");
            Place(content, view, (width - view.Width) / 2, top + primaryGap);
        }
        return content;
    }

    private static void DrawInactive(Canvas canvas, double x, double y, double width, List<DisplayTarget> inactive,
        IReadOnlyList<DisplayInfo> live, IReadOnlyDictionary<string, string> labels,
        IReadOnlyDictionary<string, int> numbers, string? selected, Action<string> select)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var target in inactive)
        {
            string name = DisplayName(target.Id, live, labels), number = DisplayNumber(target.Id, live, numbers);
            var chip = SelectButton(target, name, number, live, Same(target.Id, selected), select);
            chip.Width = Math.Min(184, width);
            chip.Height = 27;
            chip.Margin = new Thickness(0, 0, 8, 0);
            var text = Label($"{number}  {name} · {(target.Enabled ? "尺寸无效" : "已停用")}", 10, Muted);
            text.Margin = new Thickness(9, 4, 9, 4);
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            chip.Content = text;
            row.Children.Add(chip);
        }
        var scroll = new ScrollViewer { Width = width, Height = 46, Content = row, Style = null,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.HorizontalOnly, Background = Brushes.Transparent };
        Place(canvas, scroll, x, y);
    }

    private static Button SelectButton(DisplayTarget target, string name, string number, IReadOnlyList<DisplayInfo> live,
        bool selected, Action<string> select)
    {
        bool connected = live.Any(d => Same(d.Id, target.Id) && d.Connected);
        string status = (connected ? "已连接" : "未连接") + (target.Enabled ? " · 启用" : " · 已停用")
            + (target.Primary && target.Enabled ? " · 主屏" : "");
        string tip = $"{name}\n{target.Width} × {target.Height} · {target.RefreshRate} Hz\n"
            + $"X {Pixel(target.X)}，Y {Pixel(target.Y)}（像素）\n{status}\n点击选择，拖动调整位置";
        var button = new Button
        {
            Tag = target.Id, ToolTip = tip, Style = null, Template = ButtonTemplate(), FocusVisualStyle = null,
            MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(0),
            Background = ScreenFill, Foreground = TextInk, BorderBrush = selected ? Acid : Edge,
            BorderThickness = new Thickness(selected ? 1.5 : 1), Cursor = Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            UseLayoutRounding = false, SnapsToDevicePixels = false
        };
        Identify(button, "Studio.Display:" + target.Id, $"显示器 {number}，{name}，{status}，{target.Width} × {target.Height}，X {Pixel(target.X)}，Y {Pixel(target.Y)}");
        button.Click += (_, _) => select(target.Id);
        return button;
    }

    // A self-contained, square chrome: host implicit Button styles cannot add padding, radius or green fill.
    private static ControlTemplate ButtonTemplate()
    {
        var root = new FrameworkElementFactory(typeof(Grid));
        var frame = new FrameworkElementFactory(typeof(Border), "Frame");
        frame.SetBinding(Border.BackgroundProperty, TemplateBinding(Control.BackgroundProperty));
        frame.SetBinding(Border.BorderBrushProperty, TemplateBinding(Control.BorderBrushProperty));
        frame.SetBinding(Border.BorderThicknessProperty, TemplateBinding(Control.BorderThicknessProperty));
        root.AppendChild(frame);
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty, TemplateBinding(ContentControl.ContentProperty));
        presenter.SetValue(UIElement.IsHitTestVisibleProperty, false);
        root.AppendChild(presenter);
        var focus = new FrameworkElementFactory(typeof(Border), "FocusRing");
        focus.SetValue(FrameworkElement.MarginProperty, new Thickness(4));
        focus.SetValue(Border.BorderBrushProperty, Acid);
        focus.SetValue(Border.BorderThicknessProperty, new Thickness(.75));
        focus.SetValue(UIElement.IsHitTestVisibleProperty, false);
        focus.SetValue(UIElement.OpacityProperty, 0d);
        root.AppendChild(focus);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = root };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Ink(0x20, 0x21, 0x21), "Frame"));
        template.Triggers.Add(hover);
        var keyboard = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
        keyboard.Setters.Add(new Setter(UIElement.OpacityProperty, 1d, "FocusRing"));
        template.Triggers.Add(keyboard);
        return template;
    }

    private static Binding TemplateBinding(DependencyProperty property) => new(property.Name)
    { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) };

    private static TextBlock Label(string text, double size, Brush color, bool numeric = false) => new()
    {
        Text = text, FontSize = size, Foreground = color, FontFamily = numeric ? NumericFont : UiFont,
        FontWeight = FontWeights.Normal, Style = null, IsHitTestVisible = false, TextWrapping = TextWrapping.NoWrap
    };

    private static Line Segment(double x1, double y1, double x2, double y2, Brush color, double thickness) => new()
    { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = color, StrokeThickness = thickness, IsHitTestVisible = false };

    private static void Place(Canvas canvas, FrameworkElement element, double x, double y)
    { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); canvas.Children.Add(element); }

    private static void Identify(DependencyObject element, string id, string? name = null)
    { AutomationProperties.SetAutomationId(element, id); if (name != null) AutomationProperties.SetName(element, name); }

    private static Brush Ink(byte red, byte green, byte blue)
    { var brush = new SolidColorBrush(Color.FromRgb(red, green, blue)); brush.Freeze(); return brush; }

    private static bool Same(string? left, string? right) => StringComparer.OrdinalIgnoreCase.Equals(left, right);
    private static double Extent(double actual, double requested) => double.IsFinite(actual) && actual > 0 ? actual
        : double.IsFinite(requested) && requested > 0 ? requested : 0;
    private static string Pixel(double value) => (Math.Abs(value) < .001 ? 0 : value).ToString("0", CultureInfo.InvariantCulture);

    private static double NiceStep(double requested)
    {
        double power = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(1, requested))));
        double normalized = requested / power;
        return (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * power;
    }

    private static string DisplayName(string id, IReadOnlyList<DisplayInfo> live, IReadOnlyDictionary<string, string> labels)
    {
        string? alias = labels.FirstOrDefault(pair => Same(pair.Key, id)).Value;
        if (!string.IsNullOrWhiteSpace(alias)) return alias;
        var display = live.FirstOrDefault(d => Same(d.Id, id));
        return !string.IsNullOrWhiteSpace(display?.Name) ? display.Name : id;
    }

    private static string DisplayNumber(string id, IReadOnlyList<DisplayInfo> live, IReadOnlyDictionary<string, int> numbers)
    {
        int number = numbers.FirstOrDefault(pair => Same(pair.Key, id)).Value;
        if (number > 0) return number.ToString("00", CultureInfo.InvariantCulture);
        for (int i = 0; i < live.Count; i++) if (Same(live[i].Id, id)) return (i + 1).ToString("00", CultureInfo.InvariantCulture);
        return "?";
    }

    private static string AspectRatio(int width, int height)
    {
        int a = width, b = height;
        while (b != 0) { int remainder = a % b; a = b; b = remainder; }
        return $"{width / a} : {height / a}";
    }
}
