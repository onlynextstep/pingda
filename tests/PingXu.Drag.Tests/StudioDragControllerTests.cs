using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PingXu.App;
using PingXu.Core;
using static PingXu.Drag.Tests.LayoutDragTests;

namespace PingXu.Drag.Tests;

public static class StudioDragControllerTests
{
    public static void Register(Action<string, Action> test)
    {
        test("测试未加载App/Windows后端，未创建Application或Window", () =>
        {
            Check(Application.Current == null, "创建了Application");
            Check(!typeof(StudioDragControllerTests).Assembly.GetReferencedAssemblies()
                .Any(a => a.Name is "PingXu" or "PingXu.Windows"), "引入了硬件后端");
        });
        test("阈值前不capture、不提交；单击只选择一次", () =>
        {
            using var f = new Fixture(); var point = f.Center("B");
            Check(f.Controller.PointerDown(point), "未命中屏幕");
            f.Controller.PointerMove(point + new Vector(1, 1), true);
            Check(f.Capture.Captures == 0 && f.Commits == 0 && f.Selections.Count == 0, "阈值前已有副作用");
            f.Controller.PointerUp(point + new Vector(1, 1));
            Check(f.Commits == 0 && f.Selections.SequenceEqual(new[] { "B" }), "单击回调不正确");
        });
        test("capture持久外层canvas；拖动只平移预览并保持映射/按钮/字号", () =>
        {
            using var f = new Fixture(); var map = f.Map; var selected = map.Displays.Single(d => d.Id == "B");
            var button = selected.Elements.OfType<Button>().Single(); var originalContent = button.Content;
            var point = f.Center("B"); var old = f.Profile.Displays.ToArray();
            f.Controller.PointerDown(point);
            foreach (int dx in new[] { -20, -100, -250, -400, -650 })
            {
                f.Controller.PointerMove(point + new Vector(dx, 23), true); f.Layout();
                Check(ReferenceEquals(f.Capture.Owner, f.Canvas), "捕获了按钮而非canvas");
                Check(ReferenceEquals(f.Map, map) && ReferenceEquals(button.Content, originalContent), "手势中重绘了映射/注释");
                var bounds = button.TransformToAncestor(f.Canvas).TransformBounds(new Rect(button.RenderSize));
                Near(bounds.X, selected.Bounds.X + dx); Near(bounds.Y, selected.Bounds.Y + 23);
                Near(bounds.Width, selected.Bounds.Width); Near(bounds.Height, selected.Bounds.Height);
                Check(f.Commits == 0 && f.Profile.Displays.SequenceEqual(old), "预览阶段提交了草稿");
            }
            f.Controller.PointerUp(point + new Vector(-2000 * map.Scale, 0));
            Check(f.Commits == 1 && f.Selections.SequenceEqual(new[] { "B" }), "松手没有恰好提交/选择一次");
            Check(Get(f.Profile, "B").X == -1000 && !f.Capture.Held, "未解开到对侧/未释放");
            Check(f.Statuses.Count == 0, "正常拖动不应弹出通知或警告"); Legal(f.Profile, "B");
        });
        test("160×100、400×300、1100×650同一DIP/像素映射且缩放不重复", () =>
        {
            foreach (var size in new[] { (160, 100), (400, 300), (1100, 650) })
            {
                using var f = new Fixture(size.Item1, size.Item2); var map = f.Map; var p = f.Center("B");
                var display = map.Displays.Single(d => d.Id == "B"); var button = display.Elements.OfType<Button>().Single();
                Near(display.Bounds.Width, 1000 * map.Scale);
                f.Controller.PointerDown(p);
                var delta = new Vector(-2000 * map.Scale, 100 * map.Scale);
                f.Controller.PointerMove(p + delta, true); f.Layout();
                var bounds = button.TransformToAncestor(f.Canvas).TransformBounds(new Rect(button.RenderSize));
                Near(bounds.X, display.Bounds.X + delta.X); Near(bounds.Y, display.Bounds.Y + delta.Y);
                f.Controller.PointerUp(p + delta);
                Check(Get(f.Profile, "B") is { X: -1000, Y: 100 }, "缩放改变了提交坐标"); Legal(f.Profile, "B");
            }
        });
        test("每次commit/select重绘，仍可连续跨边拖动且回调不重复", () =>
        {
            using var f = new Fixture { RepaintOnCallbacks = true };
            for (int i = 0; i < 8; i++)
            {
                int destinationX = i % 2 == 0 ? -1000 : 1000;
                var p = f.Center("B"); var target = Get(f.Profile, "B");
                var delta = new Vector((destinationX - target.X) * f.Map.Scale, 0);
                f.Controller.PointerDown(p); f.Controller.PointerMove(p + delta, true); f.Controller.PointerUp(p + delta);
                Check(Get(f.Profile, "B").X == destinationX, "连续拖动原地卡死");
                Check(f.Commits == i + 1 && f.Selections.Count == i + 1, "重绘累积回调"); Legal(f.Profile, "B");
            }
        });
        foreach (string reason in new[] { "Escape", "lostcapture", "resize", "redraw", "clear children", "unload", "cancel", "dispose", "release missing", "capture failure", "canEdit", "new draft", "mutated draft" })
            test(reason + "取消不会提交/选择/丢屏，移除视觉偏移", () =>
            {
                using var f = new Fixture(); var old = f.Profile.Displays.ToArray();
                var p = f.Center("B"); var elements = f.Map.Displays.Single(d => d.Id == "B").Elements;
                var transforms = elements.Select(e => e.RenderTransform).ToArray();
                if (reason == "capture failure") f.Capture.Succeed = false;
                f.Controller.PointerDown(p); f.Controller.PointerMove(p + new Vector(-200, 40), true);
                switch (reason)
                {
                    case "Escape":
                        f.Canvas.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new TestSource(), 0, Key.Escape)
                            { RoutedEvent = Keyboard.PreviewKeyDownEvent }); break;
                    case "lostcapture": f.Capture.Lose(f.Canvas); break;
                    case "resize": f.Canvas.Width += 17; f.Layout(); break;
                    case "redraw": f.Draw(); break;
                    case "clear children": f.Canvas.Children.Clear(); break;
                    case "unload": f.Canvas.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); break;
                    case "cancel": f.Controller.Cancel(); break;
                    case "dispose": f.Controller.Dispose(); break;
                    case "release missing": f.Controller.PointerMove(p + new Vector(-210, 40), false); break;
                    case "canEdit": f.Editable = false; break;
                    case "new draft": f.Profile = f.Profile with { Name = "外部切换草稿" }; break;
                    case "mutated draft": f.Profile.Displays[0] = f.Profile.Displays[0] with { RefreshRate = 75 }; break;
                }
                f.Controller.PointerUp(p + new Vector(-400, 40));
                Check(f.Commits == 0 && f.Selections.Count == 0 && f.Statuses.Count == 0, "取消仍然触发提交/选择/状态");
                Check(!f.Capture.Held, "取消后残留捕获");
                if (reason != "mutated draft") Check(f.Profile.Displays.SequenceEqual(old), "取消改变了草稿");
                else Check(f.Profile.Displays[0].RefreshRate == 75, "覆盖了外部草稿编辑");
                for (int i = 0; i < elements.Count; i++)
                    Check(ReferenceEquals(elements[i].RenderTransform, transforms[i]), "取消没有恢复原始transform");
            });
        test("主屏视觉跟手，提交前不归零跳动；松手后其他屏平移", () =>
        {
            using var f = new Fixture(); var p = f.Center("A"); var map = f.Map;
            var delta = new Vector(2000 * map.Scale, 0);
            f.Controller.PointerDown(p); f.Controller.PointerMove(p + delta, true); f.Layout();
            var screen = map.Displays.Single(d => d.Id == "A");
            var button = screen.Elements.OfType<Button>().Single();
            var bounds = button.TransformToAncestor(f.Canvas).TransformBounds(new Rect(button.RenderSize));
            Near(bounds.X, screen.Bounds.X + delta.X);
            Check(Get(f.Profile, "B").X == 1000, "主屏预览提前改写其他屏");
            f.Controller.PointerUp(p + delta);
            Check(Get(f.Profile, "A") is { X: 0, Y: 0, Primary: true } && Get(f.Profile, "B").X == -1000, "主屏提交未重定原点");
        });
        test("重复Attach自动清理旧手势；dispose后不再拦截", () =>
        {
            using var f = new Fixture(); var p = f.Center("B");
            f.Controller.PointerDown(p); f.Controller.PointerMove(p + new Vector(-100, 20), true);
            using var replacement = StudioDragController.Attach(f.Canvas, () => f.Profile, () => true, _ => throw new Exception("不应提交"), _ => { });
            Check(!f.Capture.Held && f.Commits == 0, "替换控制器未取消旧手势");
            Check(!f.Controller.PointerDown(p), "旧控制器dispose后仍接受手势");
            replacement.Dispose(); Check(!f.Canvas.Focusable, "未恢复原始focusable状态");
        });
        test("不可编辑/停用屏/过期映射不开始拖动，保留按钮原生选择", () =>
        {
            using var f = new Fixture(); f.Editable = false;
            Check(!f.Controller.PointerDown(f.Center("B")), "不可编辑时开始拖动");
            f.Editable = true;
            f.Profile.Displays.Add(new("OFF", false, false, 0, 0, 0, 0, 0, 0)); f.Draw();
            Check(f.Map.Displays.All(d => d.Id != "OFF"), "停用屏纳入拖动映射");
            f.Profile.Displays[1] = f.Profile.Displays[1] with { X = 2000 };
            Check(!f.Controller.PointerDown(f.Center("B")), "接受了过期绘制坐标");
        });
    }

    private static void Near(double a, double b) => Check(Math.Abs(a - b) < .001, $"预期{b}，实际{a}");
    private sealed class Fixture : IDisposable
    {
        public Canvas Canvas { get; }
        public DisplayProfile Profile = Pair();
        public bool Editable = true, RepaintOnCallbacks;
        public int Commits;
        public readonly List<string> Selections = new(), Statuses = new();
        public readonly FakeCapture Capture = new();
        public StudioDragController Controller { get; }
        public StudioDragMapping Map => StudioDrawing.GetDragMapping(Canvas) ?? throw new Exception("缺少绘制映射");
        public Fixture(int width = 800, int height = 500)
        {
            Canvas = new Canvas { Width = width, Height = height }; Draw();
            Controller = new StudioDragController(Canvas, () => Profile, () => Editable,
                p => { Profile = p; Commits++; if (RepaintOnCallbacks) Draw(); },
                id => { Selections.Add(id); if (RepaintOnCallbacks) Draw(); }, Statuses.Add, Capture);
        }
        public void Draw()
        {
            Layout(); StudioDrawing.DrawLayout(Canvas, Profile, [], new Dictionary<string, string>(),
                new Dictionary<string, int> { ["A"] = 1, ["B"] = 2 }, "B", Selections.Add); Layout();
        }
        public void Layout()
        { Canvas.Measure(new Size(Canvas.Width, Canvas.Height)); Canvas.Arrange(new Rect(0, 0, Canvas.Width, Canvas.Height)); Canvas.UpdateLayout(); }
        public Point Center(string id) { var r = Map.Displays.Single(d => d.Id == id).Bounds; return new Point(r.X + r.Width / 2, r.Y + r.Height / 2); }
        public void Dispose() => Controller.Dispose();
    }
    private sealed class FakeCapture : IStudioDragCapture
    {
        public bool Held, Succeed = true;
        public int Captures;
        public Canvas? Owner;
        public bool Capture(Canvas canvas) { Captures++; Owner = canvas; return Held = Succeed; }
        public bool IsCaptured(Canvas canvas) => Held && ReferenceEquals(Owner, canvas);
        public void Release(Canvas canvas) => Lose(canvas);
        public void Lose(Canvas canvas)
        {
            Held = false;
            canvas.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.LostMouseCaptureEvent });
        }
    }
    private sealed class TestSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = new DrawingVisual();
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
