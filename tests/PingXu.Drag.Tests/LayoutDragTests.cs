using PingXu.Core;

namespace PingXu.Drag.Tests;

public static class LayoutDragTests
{
    public static void Register(Action<string, Action> test)
    {
        test("贴边连续拖动不累计误差；一次手势能自由穿越主屏", () =>
        {
            var original = Pair(); var snapshot = original.Displays.ToArray();
            var drag = Start(original, "B", .2);
            foreach (int dx in new[] { 0, -100, -500, -1000, -1500, -2000, -2500 })
            {
                var preview = drag.Preview(dx * .2, 0);
                Check(Get(preview, "B").X == 1000 + dx, "预览被贴边/碰撞夹住或累加了上次偏移");
                Check(original.Displays.SequenceEqual(snapshot), "修改了输入草稿");
            }
            var dropped = drag.Drop(-400, 0);
            Check(Get(dropped, "B").X == -1000, "未跨越到主屏左侧"); Legal(dropped, "B");
            for (int i = 0; i < 15; i++)
            {
                dropped = Start(dropped, "B", .2).Drop(0, 2);
                Check(Get(dropped, "B").Y == (i + 1) * 10, "沿接缝连续拖丢失位移"); Legal(dropped, "B");
            }
        });
        test("主屏移动将全部启用屏相对坐标平移，保留唯一主屏与停用记录", () =>
        {
            var p = Pair();
            p.Displays.Add(new("C", true, false, 2000, 0, 800, 600, 0, 75));
            p.Displays.Add(new("OFF", false, false, int.MaxValue, int.MinValue, 0, 0, 0, 0));
            var before = p.Displays.ToArray();
            var drag = Start(p, "A", .25);
            var preview = drag.Preview(125, 25);
            Check(Get(preview, "A") is { X: 0, Y: 0, Primary: true }, "预览主屏离开原点");
            Check(Get(preview, "B") is { X: 500, Y: -100 }, "预览相对位移错误");
            var result = drag.Drop(700, 0);
            Check(Get(result, "B") is { X: -1800, Y: 0 }, "主屏未跨越两屏到右侧");
            Check(Get(result, "C").X - Get(result, "B").X == 1000, "非拖动屏相对关系改变");
            Check(Get(result, "OFF") == Get(p, "OFF"), "停用屏记录被改写");
            Check(p.Displays.SequenceEqual(before), "拖动写回了输入"); Legal(result, "A");
        });
        test("DIP缩放等价、半像素舍入与极端位移无溢出", () =>
        {
            foreach (double scale in new[] { .01, .07, .2, 1, 2.5 })
            {
                var result = Start(Pair(), "b", scale).Drop(-2000 * scale, 123 * scale);
                Check(Get(result, "B") is { X: -1000, Y: 123 }, "比例换算重复或丢失");
                Legal(result, "B");
                Legal(Start(Pair(), "A", scale).Drop(double.MaxValue, -double.MaxValue), "A");
                Legal(Start(Pair(), "B", scale).Drop(-double.MaxValue, double.MaxValue), "B");
            }
            Check(Get(Start(Pair(), "B", .2).Preview(.1, -.1), "B") is { X: 1001, Y: -1 }, "半像素舍入不一致");
        });
        test("单屏不会丢失、改主屏或保留非零原点", () =>
        {
            var p = Pair(); p.Displays.RemoveAt(1);
            var result = Start(p, "A", .1).Drop(500, -500);
            Check(result.Displays.SequenceEqual(p.Displays), "单屏拖动改变了桌面");
        });
        test("无主屏/全停用/重复ID/停用主屏/无效比例不能开始拖动", () =>
        {
            Check(LayoutDrag.Start(new("p", "p", []), "A", 1) == null, "空屏被接受");
            foreach (var bad in new[]
            {
                Pair() with { Displays = Pair().Displays.Select(d => d with { Primary = false }).ToList() },
                Pair() with { Displays = Pair().Displays.Select(d => d with { Enabled = false }).ToList() },
                Pair() with { Displays = [Get(Pair(), "A"), Get(Pair(), "B") with { Id = "a" }] },
                Pair() with { Displays = [Get(Pair(), "A") with { Enabled = false }, Get(Pair(), "B")] }
            }) Check(LayoutDrag.Start(bad, "A", 1) == null, "接受了无效屏幕身份/主屏状态");
            foreach (double scale in new[] { 0, -1, double.NaN, double.PositiveInfinity })
                Check(LayoutDrag.Start(Pair(), "B", scale) == null, "接受了无效比例");
        });
        test("从重叠草稿拖出所选屏，不弹警告且不丢屏", () =>
        {
            var p = Pair(); p.Displays[1] = p.Displays[1] with { X = 250, Y = 100 };
            var result = Start(p, "B", 1).Drop(0, 0);
            Check(Get(result, "B") is { X: 250, Y: 700 }, "重叠未解开到最近边（下边距600，右边距750）"); Legal(result, "B");
        });
        test("复杂拓扑最近合法贴边与独立穷举结果一致（含凹槽、障碍截断、孔洞、竖屏）", () =>
        {
            DisplayTarget T(string id, int x, int y, int w, int h) => new(id, true, id == "A", x, y, w, h, h > w ? 90 : 0, 60);
            var fixtures = new[]
            {
                new DisplayProfile("p", "凹槽", [T("A", 0, 0, 8, 5), T("C", 8, 0, 4, 9), T("D", -5, 0, 5, 9), T("B", 0, -4, 5, 4)]),
                new DisplayProfile("p", "孔洞", [T("A", 0, 0, 9, 3), T("C", 9, 0, 3, 12), T("D", 0, 9, 9, 3), T("E", -3, 0, 3, 12), T("B", 2, -5, 4, 5)]),
                new DisplayProfile("p", "阶梯竖屏", [T("A", 0, 0, 5, 9), T("C", 5, 5, 8, 4), T("D", 9, 9, 4, 8), T("B", -7, 0, 7, 3)])
            };
            foreach (var p in fixtures)
            foreach (var id in new[] { "A", "B" })
            {
                var drag = Start(p, id, 1); var selected = Get(p, id);
                for (int wantedX = -14; wantedX <= 20; wantedX += 3)
                for (int wantedY = -14; wantedY <= 20; wantedY += 3)
                {
                    var result = drag.Drop(wantedX - selected.X, wantedY - selected.Y);
                    Legal(result, id);
                    // Recover the moved screen's pre-rebase location for a primary drag.
                    var stationary = p.Displays.First(d => d.Id != id);
                    int x = selected.Primary ? stationary.X - Get(result, stationary.Id).X : Get(result, id).X;
                    int y = selected.Primary ? stationary.Y - Get(result, stationary.Id).Y : Get(result, id).Y;
                    long actual = Distance(x, y, wantedX, wantedY), best = long.MaxValue;
                    var fixedScreens = p.Displays.Where(d => d.Id != id).ToArray();
                    for (int cx = -25; cx <= 30; cx++)
                    for (int cy = -25; cy <= 30; cy++)
                    {
                        var candidate = selected with { X = cx, Y = cy };
                        if (fixedScreens.All(o => !Overlap(candidate, o)) && fixedScreens.Any(o => Touch(candidate, o)))
                            best = Math.Min(best, Distance(cx, cy, wantedX, wantedY));
                    }
                    Check(actual == best, $"{p.Name}/{id} 希望({wantedX},{wantedY}) 实际距离{actual} 最短{best}");
                }
            }
        });
        test("32屏随机连续拖动保留全部模式与身份、无重叠、主屏原点", () =>
        {
            var p = new DisplayProfile("32", "复杂网格", Enumerable.Range(0, 32).Select(i =>
                new DisplayTarget("D" + i, true, i == 0, (i % 8) * 700, (i / 8) * 500, 700, 500, 0, 60)).ToList());
            var original = p.Displays.ToArray(); var random = new Random(7183);
            for (int i = 0; i < 150; i++)
            {
                string id = "D" + random.Next(32);
                p = Start(p, id, .1).Drop(random.Next(-900, 900), random.Next(-600, 600));
                Legal(p, id);
                for (int n = 0; n < original.Length; n++)
                    Check((p.Displays[n] with { X = original[n].X, Y = original[n].Y }) == original[n], "位置以外的属性改变");
            }
        });
        test("屏幕输入列表的后续修改不污染手势快照", () =>
        {
            var p = Pair(); var drag = Start(p, "B", 1); p.Displays.Clear();
            var result = drag.Drop(-2000, 0); Check(result.Displays.Count == 2, "未复制草稿列表"); Legal(result, "B");
        });
    }

    internal static DisplayProfile Pair() => new("p", "测试布局", [
        new("A", true, true, 0, 0, 1000, 700, 0, 60), new("B", true, false, 1000, 0, 1000, 700, 0, 60)]);
    internal static DisplayTarget Get(DisplayProfile p, string id) => p.Displays.Single(d => d.Id == id);
    internal static LayoutDrag Start(DisplayProfile p, string id, double scale) => LayoutDrag.Start(p, id, scale) ?? throw new Exception("不能开始拖动");
    internal static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static long Distance(int x, int y, int a, int b) => (long)(x - a) * (x - a) + (long)(y - b) * (y - b);
    private static bool Overlap(DisplayTarget a, DisplayTarget b) =>
        Math.Min((long)a.X + a.Width, (long)b.X + b.Width) > Math.Max(a.X, b.X) &&
        Math.Min((long)a.Y + a.Height, (long)b.Y + b.Height) > Math.Max(a.Y, b.Y);
    private static bool Touch(DisplayTarget a, DisplayTarget b) =>
        ((a.X + a.Width == b.X || b.X + b.Width == a.X) && Math.Min(a.Y + a.Height, b.Y + b.Height) > Math.Max(a.Y, b.Y)) ||
        ((a.Y + a.Height == b.Y || b.Y + b.Height == a.Y) && Math.Min(a.X + a.Width, b.X + b.Width) > Math.Max(a.X, b.X));
    internal static void Legal(DisplayProfile p, string moved)
    {
        var active = p.Displays.Where(d => d.Enabled).ToArray();
        Check(active.Length > 0 && active.Count(d => d.Primary) == 1, "丢掉了全部屏幕/唯一主屏");
        Check(active.Single(d => d.Primary) is { X: 0, Y: 0 }, "主屏不在原点");
        foreach (var a in active)
        {
            Check(Math.Abs((long)a.X) <= 100000 && Math.Abs((long)a.Y) <= 100000, "坐标越界");
            foreach (var b in active.Where(d => d.Id != a.Id)) Check(!Overlap(a, b), "最终仍有重叠");
        }
        if (active.Length > 1) Check(active.Any(d => d.Id != moved && Touch(Get(p, moved), d)), "未贴边或仅角接触");
    }
}
