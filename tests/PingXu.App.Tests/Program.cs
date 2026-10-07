using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using PingXu.Core;

namespace PingXu.App.Tests;

internal static class Program
{
    private static string ProjectDirectory([CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;
    private static readonly List<(string Name, bool Passed, string Detail)> Results = [];

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 0) { Console.Error.WriteLine("此工具不支持真实硬件模式或命令行参数。"); return 2; }
        Console.OutputEncoding = Encoding.UTF8;
        // Offscreen screenshots do not depend on GPU capture or desktop activation permissions.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var artifacts = Path.Combine(ProjectDirectory(), "artifacts");
        Directory.CreateDirectory(artifacts);
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        WindowFixture? fixture = null;
        var constructor = "未构造";
        try
        {
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/PingXu;component/Theme.xaml", UriKind.Absolute)
            });
            fixture = new WindowFixture(Path.Combine(artifacts, "isolated-data"));
            constructor = fixture.ConstructorUsed;
            var f = fixture;
            void Test(string name, Action action) => Run(name, () =>
            {
                f.Reset();
                try { action(); }
                finally { f.AssertIsolated(); }
                return "通过；显示服务调用 0";
            });

            ConfirmationIntegrationTests.Register(Test);
            ConfigurationRecoveryChecks.Register(Test);
            DiagnosticExportChecks.Register(Test);
            ShortcutBindingChecks.Register(Test);
            HotkeyEditorChecks.Register(Test);
            StatusFeedbackChecks.Register(Test);
            DialogUiTests.Register(Test);
            DisplayFailureTests.Register(Test);
            ProductUiChecks.Register(Test, f);
            WindowBoundsChecks.Register(Test);
            WorkspaceWorkflowChecks.Register(Test, f);
            Test("STA、真实 App Theme 与无 HWND 隔离", () =>
            {
                Check(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA, "非 STA");
                Check(application.Resources.Contains("Acid") && application.Resources.Contains("Primary"), "App 主题未加载");
                Check(f.Content.GetType() == typeof(Grid), "不是 MainWindow 的真实 Grid 内容");
            });
            Test("窗口实际底色非白、匹配深色主题且文字对比至少4.5:1", () =>
            {
                Console.WriteLine(VisualChecks.Background(f));
            });
            Test("Primary实际TextBlock继承按钮前景且模板对比至少4.5:1", () =>
            {
                VisualChecks.Layout(f);
                Console.WriteLine(VisualChecks.Primary(f));
            });
            Test("旧用户五种自定义场景仍可加载：三屏横竖、仅4K横竖及双带鱼", () =>
            {
                foreach (var p in f.Profiles) LayoutPlanner.Check(p, FakeDisplayService.Snapshot().Displays);
                foreach (var angle in new[] { 0, 90 })
                foreach (var only in new[] { false, true })
                    Check(f.Profiles.Any(p => p.Displays.Count(d => d.Enabled) == (only ? 1 : 3)
                        && p.Displays.Any(d => d.Id == FakeDisplayService.Left && d.Enabled && d.Rotation == angle)),
                        $"缺少 only4K={only}, angle={angle} 场景");
                Check(f.Profiles.Any(p => !p.Displays.Single(d => d.Id == FakeDisplayService.Left).Enabled
                    && p.Displays.Where(d => d.Id != FakeDisplayService.Left).All(d => d.Enabled)), "缺少双带鱼场景");
            });
            Test("五预设卡在真实布局舍入和窗口缩放后无横向溢出", () => SceneLayoutChecks.Check(f, false));
            Test("超过五张预设卡仍可横向滚动", () => SceneLayoutChecks.Check(f, true));
            Test("约106px带鱼预览保留细点阵、分辨率、比例与主屏且无溢出", () => StudioAnnotationChecks.Check(f));
            Test("方向快捷面板直接预览四个角度，保留唯一选择且不切屏",()=>
            {
                var choices=new[]{"Rotation0","Rotation90","Rotation180","Rotation270"}.Select(n=>f.Window.FindName(n) as RadioButton).ToArray();
                Check(choices.All(c=>c!=null),"缺少直观的四方向快捷面板");
                for(int i=0;i<choices.Length;i++)
                {
                    var peer = new System.Windows.Automation.Peers.RadioButtonAutomationPeer(choices[i]!);
                    ((System.Windows.Automation.Provider.ISelectionItemProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.SelectionItem)!).Select();
                    Check(f.Left.Rotation==i*90,"方向按钮未绑定真实草稿旋转");
                    Check(choices.Count(c=>c!.IsChecked==true)==1,"方向选择不唯一");
                }
            });
            Test("精确位置默认折叠，常用方向与主屏保持可见",()=>
            {
                var advanced=f.Window.FindName("AdvancedExpander") as Expander;
                Check(advanced!=null&&!advanced.IsExpanded,"高级坐标应折叠以保持常用操作可见");
                var primary=f.Get<CheckBox>("PrimaryToggle");
                Check(primary.Visibility==Visibility.Visible && f.Get<StackPanel>("Inspector").Children.Contains(primary),"主屏开关应独立于折叠面板显示");
            });
            Test("编号按硬件身份稳定，不随别名插入次序、场景排序和启停改变", () =>
            {
                var aliases = f.Get<Dictionary<string, string>>("aliases");
                aliases.Clear();
                aliases[FakeDisplayService.Right] = "右侧带鱼屏";
                aliases[FakeDisplayService.Middle] = "中间带鱼屏";
                aliases[FakeDisplayService.Left] = "左侧4K";
                foreach (var profile in f.Profiles)
                {
                    f.Invoke("SelectProfile", profile with { Displays = profile.Displays.AsEnumerable().Reverse().ToList() });
                    foreach (var (id, number) in FakeDisplayService.Snapshot().Displays.OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase).Select((d, i) => (d.Id, i + 1)))
                        Check(Equals(f.Invoke("ScreenNumber", id.ToLowerInvariant()), number), $"场景 {profile.Name} 中 {id} 编号不稳定");
                }
            });
            Test("内存中文别名同步检查器/画布，命名入口存在但不点击", () =>
            {
                const string alias = "阅读屏 · 左侧4K";
                f.Get<Dictionary<string, string>>("aliases")[FakeDisplayService.Left] = alias;
                f.SelectMonitor(FakeDisplayService.Left);
                f.Content.Measure(new Size(1400, 940));
                f.Content.Arrange(new Rect(0, 0, 1400, 940));
                f.Content.UpdateLayout();
                f.Invoke("RenderCanvas");
                Check(f.Get<TextBlock>("MonitorName").Text == alias, "检查器未使用别名");
                var rename = f.Get<Button>("RenameMonitorButton");
                Check(System.Windows.Automation.AutomationProperties.GetName(rename) == "修改屏幕名称", "修改名称图标没有可访问说明");
                Check(rename.Content is System.Windows.Shapes.Path && rename.ActualWidth >= 30 && rename.ActualHeight >= 30, "铅笔图标缺失或点击区域太小");
                var title = f.Get<TextBlock>("MonitorName");
                var titleBounds = title.TransformToAncestor(f.Content).TransformBounds(new Rect(title.RenderSize));
                var iconBounds = rename.TransformToAncestor(f.Content).TransformBounds(new Rect(rename.RenderSize));
                Check(iconBounds.Left >= titleBounds.Right && Math.Abs(iconBounds.Top - titleBounds.Top) < 8, "修改名称入口不在标题旁边");
                var card = f.Get<Canvas>("LayoutCanvas").Children.OfType<Button>().Single(b => b.ToolTip is string tip && tip.StartsWith(alias + "\n"));
                var name=System.Windows.Automation.AutomationProperties.GetName(card);
                Check(name.Contains(alias) && name.Contains("显示器 01"), "画布别名/编号不一致");
                Check(f.Get<TextBlock>("SceneHint").Text.Contains(alias), "场景提示未使用别名");
                // Do not invoke RenameMonitor_Click or Identify_Click: those have real external effects.
            });
            Test("深色 ComboBox 自定义模板加载且 Popup 保持关闭", () =>
            {
                foreach (var name in new[] { "RotationBox", "ModeBox" })
                {
                    var box = f.Get<ComboBox>(name);
                    box.ApplyTemplate();
                    Check(box.Template.FindName("PART_Popup", box) is System.Windows.Controls.Primitives.Popup { IsOpen: false }, "下拉模板缺少关闭状态的 PART_Popup");
                    Check(box.Foreground is System.Windows.Media.SolidColorBrush brush && brush.Color == ((System.Windows.Media.SolidColorBrush)application.FindResource("Ink")).Color, "下拉前景未使用主题 Ink");
                    Check(box.SelectedItem != null && !box.IsDropDownOpen, "下拉框选中值缺失或意外弹出");
                }
            });
            Test("显式编辑场景克隆草稿，不污染保存的场景", () =>
            {
                var profile = f.Profiles.First(p => p.Displays.All(d => d.Enabled));
                var original = JsonSerializer.Serialize(profile);
                f.Set("editingPresetId", profile.Id); f.Invoke("SelectProfile", profile);
                Check(!ReferenceEquals(profile.Displays, f.Draft.Displays), "场景与草稿共享可变列表");
                Check(f.Get<TextBlock>("PreviewTag").Text == "编辑预设", "缺少预设编辑提示");
                f.SelectMonitor(FakeDisplayService.Left);
                f.Rotate(1);
                Check(JsonSerializer.Serialize(profile) == original, "编辑污染了原始场景");
            });
            Test("4K方向往返：0/90/180/270/0 的宽高及主屏", () =>
            {
                foreach (var index in new[] { 1, 2, 3, 0 })
                {
                    f.Rotate(index);
                    Check(f.Left.Rotation == index * 90, "旋转角度不正确");
                    Check((f.Left.Width, f.Left.Height) == (index % 2 == 1 ? (2160, 3840) : (3840, 2160)), "旋转宽高不正确");
                    Check(f.Draft.Displays.Single(d => d.Primary).Id == FakeDisplayService.Right, "旋转改变主屏");
                    LayoutPlanner.Check(f.Draft, FakeDisplayService.Snapshot().Displays);
                }
            });
            Test("竖屏更换分辨率正确交换原生模式宽高", () =>
            {
                f.Rotate(1);
                var modes = f.Get<ComboBox>("ModeBox");
                modes.SelectedItem = modes.Items.Cast<ComboBoxItem>().Single(x => x.Tag is DisplayMode { Width: 1920, Height: 1080 });
                Check((f.Left.Width, f.Left.Height, f.Left.Rotation) == (1080, 1920, 90), "竖屏模式宽高不正确");
            });
            Test("仅4K场景：双带鱼同时停用，4K接替主屏", () =>
            {
                var profile = f.Profiles.First(p => p.Displays.Count(d => d.Enabled) == 1);
                f.Invoke("SelectProfile", profile);
                Check(f.Left.Enabled && f.Left.Primary && f.Left.X == 0 && f.Left.Y == 0, "4K未接替主屏");
                Check(f.Draft.Displays.Where(d => d.Id != FakeDisplayService.Left).All(d => !d.Enabled && !d.Primary), "双带鱼状态不一致");
            });
            Test("最后一屏不能关闭：草稿与勾选状态回退", () =>
            {
                f.Invoke("SelectProfile", f.Profiles.First(p => p.Displays.Count(d => d.Enabled) == 1));
                f.SelectMonitor(FakeDisplayService.Left);
                var before = JsonSerializer.Serialize(f.Draft);
                f.ToggleEnabled(false);
                Check(JsonSerializer.Serialize(f.Draft) == before, "最后一屏被修改");
                Check(f.Get<CheckBox>("EnabledToggle").IsChecked == true, "勾选未恢复");
                Check(f.Get<TextBlock>("StatusLabel").Text.Contains("最后"), "未解释阻止原因");
            });
            Test("改变主屏保持唯一原点与无重叠", () =>
            {
                f.SelectMonitor(FakeDisplayService.Left);
                f.Invoke("Primary_Click", f.Get<CheckBox>("PrimaryToggle"), new RoutedEventArgs());
                Check(f.Left.Primary && f.Left.X == 0 && f.Left.Y == 0, "主屏未置于原点");
                LayoutPlanner.Check(f.Draft, FakeDisplayService.Snapshot().Displays);
            });

            foreach (var handler in new[] { "Save_Click", "Manage_Click" })
                Test($"profilesReady=false：{handler}前置拒绝", () =>
                {
                    f.Set("profilesReady", false);
                    var before = JsonSerializer.Serialize(f.Profiles);
                    var draft = JsonSerializer.Serialize(f.Draft);
                    int windows = application.Windows.Count;
                    f.Invoke(handler, f.Window, new RoutedEventArgs());
                    Check(f.Get<TextBlock>("StatusLabel").Text.Contains("禁止"), "缺少保护原文件的拒绝提示");
                    Check(JsonSerializer.Serialize(f.Profiles) == before && JsonSerializer.Serialize(f.Draft) == draft, "拒绝后预设或草稿变化");
                    Check(application.Windows.Count == windows, "意外创建对话窗口");
                    Check(!Directory.Exists(Path.Combine(artifacts, "isolated-data")), "拒绝分支不应创建数据目录或文件");
                });
            foreach(var failure in new[]{"revision","unreadable","busy"})Test("实际写入边界拒绝模态重入："+failure,()=>
            {
                var before=JsonSerializer.Serialize(f.Profiles);
                int expectedRevision=f.Get<int>("libraryRevision");
                if(failure=="revision")f.Set("libraryRevision",expectedRevision+1);
                if(failure=="unreadable")f.Set("profilesReady",false);
                if(failure=="busy")f.Set("busy",true);
                bool rejected=false;
                try{f.Invoke("CommitProfiles",new List<DisplayProfile>(),expectedRevision);}catch(System.Reflection.TargetInvocationException ex)when(ex.InnerException is InvalidOperationException){rejected=true;}
                Check(rejected,"实际持久化入口未拒绝失效操作");
                Check(JsonSerializer.Serialize(f.Profiles)==before,"拒绝后预设集合变化");
                Check(!Directory.Exists(Path.Combine(artifacts,"isolated-data")),"拒绝后发生持久化写入");
            });
            Test("recoveryBlocked=true：异步ApplyDraft前置返回且服务零调用", () =>
            {
                f.Set("recoveryBlocked", true);
                var before = JsonSerializer.Serialize(f.Draft);
                int windows = application.Windows.Count;
                // Helper uses reflection and awaits the returned Task; the completion guard prevents STA pumping/deadlocks.
                f.InvokeBlockedApplyAsync().GetAwaiter().GetResult();
                Check(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA, "测试离开了STA线程");
                Check(f.Service.Calls.Count == 0 && !f.Get<bool>("busy"), "恢复锁定后仍进入显示操作");
                Check(f.Get<TextBlock>("StatusLabel").Text.Contains("恢复状态未确认"), "未提示恢复锁定");
                Check(JsonSerializer.Serialize(f.Draft) == before && application.Windows.Count == windows, "拒绝后草稿或窗口改变");
                Check(!Directory.Exists(Path.Combine(artifacts, "isolated-data")), "恢复拒绝分支意外创建文件");
            });

            var busyActions = new Dictionary<string, Action>
            {
                ["场景选择"] = () => f.Invoke("SelectProfile", f.Profiles.Last()),
                ["启停"] = () => f.ToggleEnabled(false),
                ["旋转"] = () => f.Rotate(1),
                ["主屏"] = () => f.Invoke("Primary_Click", f.Get<CheckBox>("PrimaryToggle"), new RoutedEventArgs()),
                ["位置"] = () => { f.Get<TextBox>("XBox").Text = "999"; f.Invoke("Position_Click", f.Window, new RoutedEventArgs()); },
                ["对齐"] = () => f.Invoke("Align_Click", f.Window, new RoutedEventArgs()),
                ["模式"] = () => f.Get<ComboBox>("ModeBox").SelectedItem = f.Get<ComboBox>("ModeBox").Items.Cast<ComboBoxItem>().Last(),
                ["通用更新"] = () => f.Invoke("UpdateTarget", (Func<DisplayTarget, DisplayTarget>)(d => d with { Width = 999 }), false)
            };
            foreach (var (name, action) in busyActions)
                Test("busy 阻止草稿修改：" + name, () =>
                {
                    var before = JsonSerializer.Serialize(f.Draft);
                    var selected = f.Get<string>("selected");
                    f.Set("busy", true);
                    action();
                    Check(JsonSerializer.Serialize(f.Draft) == before, "busy 时草稿被修改");
                    Check(f.Get<string>("selected") == selected, "busy 时选择被修改");
                    Check(!f.Get<bool>("dirty"), "busy 时错误标记为 dirty");
                });
            foreach (var (width, height) in new[] { (1400, 940), (1060, 720) })
                Run($"离屏截图 ui-{width}.png", () => f.Render(Path.Combine(artifacts, $"ui-{width}.png"), width, height));
            Run("原型同状态对照 ui-reference.png", () => f.Render(Path.Combine(artifacts, "ui-reference.png"), 1488, 1058, 0));
            foreach (var (width, height) in new[] { (3440, 1440), (3840, 2160) })
                Run($"大屏实际UI离屏对照 ui-{width}.png", () => f.Render(Path.Combine(artifacts, $"ui-{width}.png"), width, height, 0));
            Run("结束时无真实窗口生命周期与服务调用", () => { f.AssertIsolated(); return "通过"; });
        }
        catch (Exception e) { Results.Add(("测试工具初始化/执行", false, e.GetBaseException().ToString())); }
        finally { fixture?.Dispose(); }

        var failures = Results.Count(r => !r.Passed);
        var report = new StringBuilder("# 屏搭 WPF 假硬件 UI 冒烟运行报告\n\n")
            .AppendLine($"运行时间：{DateTimeOffset.Now:O}")
            .AppendLine($"\n结果：{Results.Count - failures}/{Results.Count} 通过，{failures} 失败。")
            .AppendLine($"\n运行环境：{Environment.OSVersion}；.NET {Environment.Version}；STA。")
            .AppendLine($"\n构造注入：`{constructor}`")
            .AppendLine("\n范围：仅假硬件 UI 与草稿行为。真实 MainWindow.Content + pack URI App Theme，96 DPI 离屏渲染；没有启动 App.Program/MainWindow.Show/Application.Run。")
            .AppendLine("\n模拟三屏：左3840×2160、中3440×1440、右3440×1440；右主屏。中右型号相同、假ID不同。60 Hz 为测试假设，不是硬件检测结果。")
            .AppendLine($"\n假服务调用记录：{(fixture == null ? "未创建" : JsonSerializer.Serialize(fixture.Service.Calls))}")
            .AppendLine("\n未执行 Loaded、托盘、系统快捷键、启动项、显示应用/恢复、窗口救援或真实输入；私有方法调用受白名单约束。构造后立即停止硬件定时器，可选数据目录重定向到测试 artifacts/isolated-data。")
            .AppendLine("\n别名仅在内存 Dictionary 中注入，不读写真实 Preferences 文件，不执行 Preferences.Load/Save、RenameMonitor_Click 或 Identify_Click。未验证跨进程别名持久化、实际屏幕识别浮层或下拉弹出交互。")
            .AppendLine("\n最终追加：profilesReady=false 的 Save/Manage 拒绝分支；recoveryBlocked=true 的 ApplyDraft 通过反射取得 Task 并 await 已完成任务，禁止消息循环等待。验证拒绝提示、草稿/预设不变、窗口数不增加、服务调用为0、隔离数据目录未创建。ProfileStore 始终替换为测试目录路径；不读写真实数据目录。")
            .AppendLine("\n| 测试 | 结果 | 详情 |\n|---|---|---|");
        foreach (var r in Results) report.AppendLine($"| {r.Name} | {(r.Passed ? "PASS" : "FAIL")} | {r.Detail.Replace("|", "\\|").Replace("\r", "").Replace("\n", "<br>")} |");
        report.AppendLine("\n截图：[ui-1400.png](ui-1400.png)、[ui-1060.png](ui-1060.png)。尺寸指 Content 画布，不包含系统标题栏。")
            .AppendLine("\n人工视觉复核与首次构建环境说明：[visual-review.md](visual-review.md)。冒烟通过不代表视觉问题已修复。")
            .AppendLine("\n限制：未验证真实热插拔、Guardian回退、显示驱动、跨屏DPI、真实焦点/快捷键/托盘、读屏或鼠标键盘操作。busy 用例验证数据修改守卫，不代表控件视觉禁用或完整异步事务通过。反射缝依赖私有成员，源代码改变时应显式失败并更新测试。PNG尺寸与画布元素检查不等于无裁切或视觉质量通过；请结合截图人工检查。");
        File.WriteAllText(Path.Combine(artifacts, "run-report.md"), report.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"{Results.Count - failures}/{Results.Count} PASS; report: {Path.Combine(artifacts, "run-report.md")}");
        application.Shutdown(failures == 0 ? 0 : 1);
        return failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Func<string> action)
    {
        try { var detail = action(); Results.Add((name, true, detail)); Console.WriteLine($"PASS {name}: {detail}"); }
        catch (Exception e) { var detail = e.GetBaseException().Message; Results.Add((name, false, detail)); Console.WriteLine($"FAIL {name}: {detail}"); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
