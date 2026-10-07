using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PingXu.App;
using PingXu.Core;

namespace PingXu.SceneManager.Tests;

public static class SceneManagerTests
{
    public static void Register(Action<string, Action> test)
    {
        test("Manage旧三参数与新五参数兼容，工厂不创建窗口或执行外部动作", () =>
        {
            var method = typeof(Dialogs).GetMethod(nameof(Dialogs.Manage))!;
            Check(method.GetParameters().Length == 5 && method.GetParameters().Skip(3).All(p => p.IsOptional), "Manage可选参数不兼容");
            Action<Window, List<DisplayProfile>, Action<List<DisplayProfile>>> oldCall = (w, p, save) => Dialogs.Manage(w, p, save);
            Action<Window, List<DisplayProfile>, Action<List<DisplayProfile>>, Action, Action<DisplayProfile>> newCall =
                (w, p, save, create, edit) => Dialogs.Manage(w, p, save, create, edit);
            var source = Library(); string before = Json(source);
            var root = Dialogs.CreateSceneManagerContent(source, _ => throw new Exception("构建不应保存"),
                () => throw new Exception("构建不应新建"), _ => throw new Exception("构建不应编辑"),
                () => throw new Exception("构建不应关闭"), () => throw new Exception("构建不应导入"), _ => throw new Exception("构建不应导出"));
            Layout(root, new Size(680, 620));
            Check(Application.Current == null && PresentationSource.FromVisual(root) == null, "连接了实际窗口");
            Check(!typeof(SceneManagerTests).Assembly.GetReferencedAssemblies().Any(a => a.Name is "PingXu" or "PingXu.Windows"), "引入了主程序/后端");
            Check(Json(source) == before, "构建修改了输入");
        });
        test("主入口新建、每行四项操作、唯一AutomationId，迁移默认折叠", () =>
        {
            var root = Create();
            Check((string)Find<Button>(root, "New").Content == "新建预设", "主入口不是新建");
            foreach (var p in Library()) foreach (string action in new[] { "Edit", "Rename", "Copy", "Delete" })
                Check(Find<Button>(root, action + ":" + p.Id) != null, "缺少行操作");
            var backup = Find<Expander>(root, "Backup");
            Check((string)backup.Header == "备份与迁移" && !backup.IsExpanded, "迁移不是默认折叠");
            Check(Descendants(backup).Contains(Find<Button>(root, "Import")) && Descendants(backup).Contains(Find<Button>(root, "Export")), "导入导出未放在折叠区");
            var ids = Descendants(root).Select(AutomationProperties.GetAutomationId).Where(s => s.Length > 0).ToArray();
            Check(ids.Distinct().Count() == ids.Length, "AutomationId重复");
        });
        test("新建先close后callback，不保存且重复触发仅一次", () =>
        {
            var order = new List<string>();
            var root = Dialogs.CreateSceneManagerContent(Library(), _ => throw new Exception("新建不能在管理页保存"),
                () => order.Add("new"), close: () => order.Add("close"));
            Click(root, "New"); Click(root, "New");
            Check(order.SequenceEqual(new[] { "close", "new" }), "新建回调顺序/幂等性不正确");
        });
        test("编辑先close再回传隔离布局，保留ID/模式且不保存", () =>
        {
            var source = Library(); var before = Json(source); var order = new List<string>();
            var root = Dialogs.CreateSceneManagerContent(source, _ => throw new Exception("编辑入口不应保存"),
                editPreset: p => { order.Add("edit"); Check(p == source[1] with { Displays = p.Displays }, "编辑错预设");
                    Check(p.Displays.SequenceEqual(source[1].Displays), "编辑参数丢失模式"); p.Displays.Clear(); },
                close: () => order.Add("close"));
            Click(root, "Edit:two"); Check(order.SequenceEqual(new[] { "close", "edit" }), "编辑回调顺序错误");
            Check(Json(source) == before, "编辑回调污染了源库");
        });
        test("缺失可选回调禁用新建/编辑/迁移，旧入口仍能复制改名删除", () =>
        {
            int saves = 0; var root = Dialogs.CreateSceneManagerContent(Library(), _ => saves++);
            foreach (string id in new[] { "New", "Edit:one", "Import", "Export" }) Check(!Find<Button>(root, id).IsEnabled, "空回调仍可操作");
            Click(root, "New"); Click(root, "Edit:one"); Click(root, "Import"); Click(root, "Export");
            Check(saves == 0, "空回调产生保存"); Click(root, "Copy:one"); Check(saves == 1, "旧入口复制失效");
        });
        test("关闭失败不进入编辑、callback失败显示且不删除场景", () =>
        {
            int creates = 0;
            var root = Dialogs.CreateSceneManagerContent(Library(), _ => { }, () => creates++,
                close: () => throw new InvalidOperationException("模拟关闭失败"));
            Click(root, "New"); Check(creates == 0 && Status(root).Contains("模拟关闭失败"), "关闭失败后仍继续");
            var failed = Dialogs.CreateSceneManagerContent(Library(), _ => { }, () => throw new InvalidOperationException("模拟编辑失败"));
            Click(failed, "New"); Check(Status(failed).Contains("模拟编辑失败") && Rows(failed) == 2 && Find<Button>(failed, "New").IsEnabled, "失败丢库/不可恢复");
        });
        test("改名行内展开、trim后保存，只改名称并保留源列表", () =>
        {
            List<DisplayProfile>? saved = null; var source = Library(); string before = Json(source);
            var root = Create(source, p => saved = p);
            Rename(root, "one", "  专注工作  ");
            Check(saved != null && saved.Count == 2 && saved[0].Name == "专注工作" && saved[0].Id == "one", "改名结果错误");
            Check(saved![0].Displays.SequenceEqual(source[0].Displays) && Json(source) == before, "改名污染模式/源库");
            Check(Find<TextBlock>(root, "Name:one").Text == "专注工作", "改名未刷新行");
        });
        test("空白/超长名称不保存；取消和原名均不写库", () =>
        {
            int saves = 0; var root = Create(save: _ => saves++);
            foreach (string name in new[] { "   ", new string('名', 61) })
            { Rename(root, "one", name); Check(Status(root).Contains("1至60"), "名称未验证"); }
            Click(root, "RenameCancel:one");
            Check(Find<TextBox>(root, "RenameInput:one").Parent is UIElement { Visibility: Visibility.Collapsed }, "取消未收起");
            Rename(root, "one", Library()[0].Name); Check(saves == 0, "无有效更改仍保存");
        });
        test("复制生成独立ID/显示列表、唯一副本名且长名不超60字", () =>
        {
            var source = Library(); source[0] = source[0] with { Name = new string('长', 60) };
            List<DisplayProfile>? saved = null; var root = Create(source, p => saved = p);
            for (int i = 0; i < 3; i++) Click(root, "Copy:one");
            Check(saved is { Count: 5 }, "复制数量不正确");
            var copies = saved ?? throw new Exception("没有保存复制结果");
            Check(copies.Select(p => p.Id).Distinct().Count() == 5 && copies.Select(p => p.Name).Distinct().Count() == 5, "副本身份/名称重复");
            foreach (var copy in copies.Skip(2))
            {
                Check(copy.Name.Length <= 60 && copy.Displays.SequenceEqual(source[0].Displays), "副本名称/布局丢失");
                Check(!ReferenceEquals(copy.Displays, copies[0].Displays), "副本共享显示列表");
            }
        });
        test("删除必须行内确认，可取消且不能通过隐藏确认按钮绕过", () =>
        {
            int saves = 0; List<DisplayProfile>? saved = null;
            var root = Create(save: p => { saves++; saved = p; });
            Click(root, "ConfirmDelete:one"); Check(saves == 0, "隐藏确认按钮绕过确认");
            Click(root, "Delete:one"); Check(saves == 0 && Find<TextBlock>(root, "DeletePrompt:one").Text.Contains("日常"), "删除没有等待确认");
            Click(root, "CancelDelete:one"); Click(root, "ConfirmDelete:one"); Check(saves == 0, "取消后仍能确认");
            Click(root, "Delete:one"); Click(root, "ConfirmDelete:one");
            Check(saves == 1 && saved!.Single().Id == "two" && Rows(root) == 1, "删除错行或未刷新");
            Click(root, "Delete:two"); Click(root, "ConfirmDelete:two");
            Check(Rows(root) == 0 && Find<TextBlock>(root, "Empty").Text.Contains("新建预设"), "空库没有引导");
        });
        foreach (string operation in new[] { "rename", "copy", "delete", "import" })
            test(operation + "保存回调修改参数后抛错，保留原库并可重试", () =>
            {
                var source = Library(); string before = Json(source); int calls = 0; List<DisplayProfile>? accepted = null;
                var root = Create(source, candidate =>
                {
                    calls++;
                    if (calls == 1) { candidate[0].Displays.Clear(); candidate.Clear(); throw new IOException("模拟磁盘失败"); }
                    accepted = candidate;
                }, import: () => Json(new[] { Library()[0] }));
                void Act()
                {
                    switch (operation)
                    {
                        case "rename": Rename(root, "one", "改名后"); break;
                        case "copy": Click(root, "Copy:one"); break;
                        case "delete": Click(root, "Delete:one"); Click(root, "ConfirmDelete:one"); break;
                        case "import": Click(root, "Import"); break;
                    }
                }
                Act();
                Check(calls == 1 && Rows(root) == 2 && Json(source) == before && Status(root).Contains("模拟磁盘失败"), "失败覆盖了原库或未显示");
                Check(Find<TextBlock>(root, "Name:one").Text == "日常", "失败后显示了未保存值");
                Act(); Check(calls == 2 && accepted != null && accepted.All(p => p.Displays.Count == 2), "重试丢失布局");
            });
        test("成功保存后回调持有列表的后续修改不能污染显示库", () =>
        {
            List<DisplayProfile>? argument = null; string? exported = null;
            var root = Create(save: p => argument = p, export: json => { exported = json; return true; });
            Click(root, "Copy:one"); argument![0].Displays.Clear(); argument.Clear();
            Click(root, "Export"); Check(ProfileStore.Parse(exported!).Count == 3, "保存参数仍与内部库共用");
        });
        test("回调重入和旧行残留点击不会重复操作", () =>
        {
            FrameworkElement? root = null; int saves = 0;
            root = Create(save: _ => { saves++; Click(root!, "Copy:one"); });
            var stale = Find<Button>(root, "Copy:one"); Click(root, "Copy:one");
            stale.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(saves == 1 && Rows(root) == 3, "重入或过期行重复保存");
        });
        test("导入追加、不覆盖同ID预设；取消/空文件列表不保存", () =>
        {
            List<DisplayProfile>? saved = null; int calls = 0;
            string? incoming = null;
            var root = Create(save: p => { saved = p; calls++; }, import: () => incoming);
            Click(root, "Import"); Check(calls == 0, "取消仍保存");
            incoming = "[]"; Click(root, "Import"); Check(calls == 0, "空列表仍保存");
            incoming = Json(Library()); Click(root, "Import");
            Check(calls == 1 && saved is { Count: 4 } && saved.Select(p => p.Id).Distinct().Count() == 4, "导入覆盖了ID");
            Check(saved![0].Id == "one" && saved[1].Id == "two", "原预设被替换");
        });
        test("导入损坏/过大/非法布局/读文件异常均显示错误且保留库", () =>
        {
            foreach (string json in new[] { "broken", "null", new string('x', 2_000_001),
                Json(new[] { Library()[0] with { Displays = [] } }),
                Json(new[] { Library()[0] with { Displays = Library()[0].Displays.Select(d => d with { Enabled = false }).ToList() } }) })
            {
                int saves = 0; var root = Create(save: _ => saves++, import: () => json);
                Click(root, "Import"); Check(saves == 0 && Rows(root) == 2 && Status(root).Contains("导入失败"), "非法导入未保护场景库");
            }
            var failed = Create(import: () => throw new IOException("模拟读取失败"));
            Click(failed, "Import"); Check(Status(failed).Contains("模拟读取失败") && Rows(failed) == 2, "读取失败未显示");
        });
        test("100个预设上限同时约束复制和导入", () =>
        {
            var source = Enumerable.Range(0, 100).Select(i => Library()[0] with { Id = "p" + i }).ToList();
            int saves = 0; var root = Create(source, _ => saves++, () => Json(Library()));
            Click(root, "Copy:p0"); Check(Status(root).Contains("100"), "复制未限制数量");
            Click(root, "Import"); Check(Status(root).Contains("100") && saves == 0 && Rows(root) == 100, "超限导入丢库");
        });
        test("导出使用最新已保存快照，取消/异常不保存不丢库", () =>
        {
            int saves = 0; string? output = null; bool success = false;
            var root = Create(save: _ => saves++, export: json => { output = json; return success; });
            Rename(root, "one", "最新名称"); Click(root, "Export");
            Check(ProfileStore.Parse(output!)[0].Name == "最新名称" && !Status(root).Contains("已导出"), "导出取消误报/快照过期");
            success = true; Click(root, "Export"); Check(Status(root).Contains("已导出 2") && saves == 1, "导出写回了库");
            var failed = Create(export: _ => throw new IOException("模拟导出失败"));
            Click(failed, "Export"); Check(Status(failed).Contains("模拟导出失败") && Rows(failed) == 2, "导出失败损坏库");
        });
        test("外部源列表修改不影响已打开的场景库快照", () =>
        {
            var source = Library(); string? exported = null;
            var root = Create(source, export: json => { exported = json; return true; });
            source[0].Displays.Clear(); source.Clear(); Click(root, "Export");
            Check(ProfileStore.Parse(exported!).Count == 2, "源列表修改渗透到工厂");
        });
        test("实际主题下360/680/1000宽度可resize，长名/行操作/确认/备份不水平溢出", () =>
        {
            foreach (var size in new[] { new Size(360, 300), new Size(680, 620), new Size(1000, 800) })
            {
                var source = Library(); source[0] = source[0] with { Name = new string('长', 60) };
                var root = Create(source);
                string theme = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "../../src/PingXu.App/Theme.xaml"));
                root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(theme) });
                Layout(root, size); Find<Expander>(root, "Backup").IsExpanded = true;
                Click(root, "Delete:one"); Layout(root, size);
                foreach (string id in new[] { "New", "Name:one", "Edit:one", "Rename:one", "Copy:one", "Delete:one", "DeletePrompt:one", "ConfirmDelete:one", "Import", "Export" })
                    VisibleByScrolling((ScrollViewer)root, size, id);
                Click(root, "Rename:two"); Layout(root, size);
                VisibleByScrolling((ScrollViewer)root, size, "RenameInput:two");
                VisibleByScrolling((ScrollViewer)root, size, "RenameSave:two");
            }
        });
    }

    private static FrameworkElement Create(List<DisplayProfile>? source = null, Action<List<DisplayProfile>>? save = null,
        Func<string?>? import = null, Func<string, bool>? export = null) =>
        Dialogs.CreateSceneManagerContent(source ?? Library(), save ?? (_ => { }), () => { }, _ => { },
            () => { }, import ?? (() => null), export ?? (_ => false));
    private static List<DisplayProfile> Library() => [
        new("one", "日常", [new("A", true, true, 0, 0, 1920, 1080, 0, 60), new("B", true, false, 1920, 0, 1080, 1920, 90, 75)]),
        new("two", "阅读", [new("A", true, true, 0, 0, 1920, 1080, 0, 60), new("B", false, false, 1920, 0, 1080, 1920, 90, 75)])];
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, ProfileStore.Json);
    private static void Rename(FrameworkElement root, string id, string name)
    { Click(root, "Rename:" + id); Find<TextBox>(root, "RenameInput:" + id).Text = name; Click(root, "RenameSave:" + id); }
    private static void Click(FrameworkElement root, string id) => Find<Button>(root, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static string Status(FrameworkElement root) => Find<TextBlock>(root, "Status").Text;
    private static int Rows(FrameworkElement root) => Descendants(root).Count(e => AutomationProperties.GetAutomationId(e).StartsWith("SceneManager.Row:"));
    private static T Find<T>(DependencyObject root, string id) where T : FrameworkElement => Descendants(root).OfType<T>()
        .Single(e => AutomationProperties.GetAutomationId(e) == "SceneManager." + id);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var next in Descendants(child)) yield return next;
    }
    private static void Layout(FrameworkElement root, Size size)
    { root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout(); }
    private static void VisibleByScrolling(ScrollViewer root, Size size, string id)
    {
        var element = Find<FrameworkElement>(root, id);
        double y = element.TransformToAncestor((FrameworkElement)root.Content).Transform(new Point()).Y;
        root.ScrollToVerticalOffset(y); Layout(root, size);
        var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        Check(bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= -.1 && bounds.Right <= root.ViewportWidth + .1 &&
            bounds.Top >= -.1 && bounds.Bottom <= root.ActualHeight + .1 && root.ScrollableWidth < .1,
            $"{id} {size} 无法完整显示：{bounds}，视口{root.ViewportWidth}/{root.ActualHeight}");
        if (element is TextBlock text)
        {
            var probe = new TextBlock { Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize,
                FontWeight = text.FontWeight, FontStyle = text.FontStyle, FontStretch = text.FontStretch,
                UseLayoutRounding = text.UseLayoutRounding, TextWrapping = TextWrapping.Wrap };
            probe.Measure(new Size(text.ActualWidth, double.PositiveInfinity));
            Check(probe.DesiredSize.Height <= text.ActualHeight + .1,
                $"{id} {size} 长名称/说明文字被裁剪：所需{probe.DesiredSize.Height}，实际{text.ActualHeight}，宽{text.ActualWidth}");
        }
    }
    private static string SourcePath([CallerFilePath] string path = "") => path;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
