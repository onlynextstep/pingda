using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PingXu.Core;

namespace PingXu.App.Tests;

internal static class WorkspaceWorkflowChecks
{
    public static void Register(Action<string, Action> test, WindowFixture f)
    {
        void Case(string name, Action<Boundary> action) => test("工作区流程：" + name, () =>
        {
            using var callbacks = new WorkflowCallbacks();
            using var boundary = new Boundary(f);
            action(boundary);
            boundary.AssertIsolated();
        });

        Case("新预设默认名称自动避开已有名称", b =>
        {
            f.Profiles.Add(f.Profiles[0] with { Id = "default-name-test", Name = "新预设" });
            Click(f.Get<Button>("NewPresetButton"));
            Check(f.Get<TextBox>("PresetNameBox").Text != "新预设", "默认名称与已有预设重复");
            b.NoSwitch();
        });

        Case("屏幕改名在原位输入，选择另一屏不会误改目标", b =>
        {
            Click(f.Get<Button>("RenameMonitorButton"));
            Check(f.Get<StackPanel>("MonitorRenamePanel").Visibility == Visibility.Visible, "没有显示原位编辑框");
            Check(f.Get<TextBlock>("MonitorName").Visibility == Visibility.Collapsed, "编辑框没有替换标题");
            f.Get<TextBox>("MonitorRenameBox").Text = "未保存名称";
            var other = f.Draft.Displays.First(d => d.Id != f.Get<string>("selected"));
            f.Set("selected", other.Id); f.Invoke("PopulateInspector");
            Check(f.Get<StackPanel>("MonitorRenamePanel").Visibility == Visibility.Collapsed, "切换屏幕后仍可保存旧屏名称");
            b.NoSwitch();
        });

        Case("主动取消脏草稿直接恢复工作区，不打开确认窗口或切屏", b =>
        {
            var library = Json(f.Profiles);
            f.Set("dirty", true);
            f.Invoke("RenderCanvas");
            Click(f.Get<Button>("CancelEditButton"));
            Check(!f.Get<bool>("dirty") && f.Get<string?>("editingPresetId") == null, "取消后仍残留修改");
            Check(Json(f.Profiles) == library && !File.Exists(b.ProfilePath), "取消修改写入了预设库");
            Check(DisplayPresentation.SameLayout(f.Draft.Displays, f.Get<DesktopSnapshot>("snapshot").Displays.Select(Target)), "取消后没有恢复当前布局");
            b.NoSwitch();
        });

        Case("统一编辑入口在原窗口选择预设，不切换硬件", b =>
        {
            var current = new DisplayProfile("edit-current", "当前预设", f.Get<DesktopSnapshot>("snapshot").Displays.Select(Target).ToList());
            f.Profiles.Insert(0, current);
            f.Get<Button>("ManagePresetsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(f.Get<string?>("editingPresetId") == current.Id, "点击编辑后未直接载入当前预设");
            Check(f.Get<TextBox>("PresetNameBox").Text == current.Name, "名称未载入");
            var target = DifferentScene(f);
            var button = SceneButton(f, target.Id);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
            ((WorkflowCallbacks)SynchronizationContext.Current!).Complete();
            Check(f.Get<string>("editingPresetId") == target.Id, "卡片未载入编辑器");
            Check(f.Get<Button>("ApplyButton").Visibility == Visibility.Collapsed, "编辑中仍显示应用布局");
            b.NoSwitch();
            f.Get<Button>("ManagePresetsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!f.Get<bool>("selectingPreset"), "退出后仍处于选择模式");
        });

        Case("布局操作固定右侧，滚动屏幕设置不移动按钮", b =>
        {
            f.Invoke("RenderCanvas"); VisualChecks.Layout(f);
            var strip = f.Get<StackPanel>("SceneStrip");
            Check(strip.Children.OfType<Button>().All(card => !VisualChecks.Descendants(card).OfType<Button>().Any()), "每张卡片仍含额外编辑按钮");
            Check(f.Get<Button>("ManagePresetsButton").Visibility == Visibility.Visible, "缺少统一管理入口");
            var apply = f.Get<Button>("ApplyButton");
            Check(apply.Visibility == Visibility.Visible && !apply.IsEnabled, "未编辑时应用按钮不应消失或可点击");
            foreach (var size in new[] { new Size(1060, 720), new Size(1488, 1058), new Size(3440, 1440) })
            {
                VisualChecks.Layout(f, (int)size.Width, (int)size.Height);
                var canvas = f.Get<Canvas>("LayoutCanvas");
                var start = apply.TransformToAncestor(f.Content).Transform(new Point());
                var canvasRight = canvas.TransformToAncestor(f.Content).Transform(new Point(canvas.ActualWidth, 0));
                Check(start.X > canvasRight.X, "应用按钮仍占据画布而不是右栏");
                var scroll = f.Get<ScrollViewer>("InspectorScroll");
                f.Get<Expander>("AdvancedExpander").IsExpanded = true;
                VisualChecks.Layout(f, (int)size.Width, (int)size.Height);
                scroll.ScrollToEnd(); VisualChecks.Layout(f, (int)size.Width, (int)size.Height);
                var end = apply.TransformToAncestor(f.Content).Transform(new Point());
                Check(Math.Abs(start.Y-end.Y) < 1, "设置展开或滚动推走布局操作");
                Check(end.Y + apply.ActualHeight <= size.Height && apply.ActualWidth >= 220, "小窗口按钮被截断");
                var shelfBottom = f.Get<ScrollViewer>("ScenesScroll").TransformToAncestor(f.Content)
                    .Transform(new Point(0, f.Get<ScrollViewer>("ScenesScroll").ActualHeight));
                Check(size.Height - shelfBottom.Y < 25, "预设底部仍有整条操作栏占高");
            }
            f.Get<Expander>("AdvancedExpander").IsExpanded = false;
            f.Get<ScrollViewer>("InspectorScroll").ScrollToTop();
            b.NoSwitch();
        });

        Case("等待放弃编辑确认时，快捷键重入不得覆盖原请求", b =>
        {
            var button = SceneButton(f, DifferentScene(f).Id);
            f.Set("pendingSceneId", "existing-request");
            try
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
                ((WorkflowCallbacks)SynchronizationContext.Current!).Complete();
                Check(f.Get<string>("pendingSceneId") == "existing-request" && b.Service.Captures == 0,
                    "重入请求覆盖了待确认请求或读取了硬件");
                b.NoSwitch();
            }
            finally { f.Set("pendingSceneId", null); f.Invoke("RenderScenes"); }
        });

        foreach (bool succeeds in new[] { false, true })
        Case("点击立即高亮，当前标记仅随实测更新：" + succeeds, b =>
        {
            var actual = f.Get<DesktopSnapshot>("snapshot");
            var current = new DisplayProfile("feedback-current", "原布局", actual.Displays.Select(Target).ToList());
            f.Profiles.Add(current);
            var target = DifferentScene(f);
            using var release = new ManualResetEventSlim();
            b.Service.OnCapture = () =>
            {
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("反馈测试采集未释放");
                return b.Service.Live;
            };
            bool Yellow(string id) => SceneButton(f, id).Background is System.Windows.Media.SolidColorBrush brush
                && brush.Color == System.Windows.Media.Color.FromRgb(220, 255, 66);
            bool Current(string id) => AutomationProperties.GetName(SceneButton(f, id)).Contains("当前");
            f.Set("switchLayout", (Func<DisplayProfile, Task>)(p =>
            {
                Check(Yellow(p.Id) && !Yellow(current.Id), "切换过程中丢失点击高亮");
                Check(Current(current.Id) && !Current(p.Id), "未回读就提前移动当前标记");
                if (succeeds) f.Set("snapshot", LiveFor(p));
                return Task.CompletedTask;
            }));
            var button = SceneButton(f, target.Id);
            try
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
                Check(Yellow(target.Id) && !Yellow(current.Id), "点击首次await前未切换主题黄");
                Check(Current(current.Id) && !Current(target.Id), "点击提前改变当前标记");
                var pending = SceneButton(f, target.Id);
                pending.ApplyTemplate();
                Check(!pending.IsEnabled && pending.Template.FindName("Chrome", pending) is Border { Opacity: 1 }, "处理中卡片变暗或允许重复点击");
            }
            finally { release.Set(); ((WorkflowCallbacks)SynchronizationContext.Current!).Complete(); }
            Check(Yellow(succeeds ? target.Id : current.Id), "结束后高亮未跟随实际结果");
            Check(Current(succeeds ? target.Id : current.Id), "结束后当前标记错误");
        });

        Case("实测七屏拒绝场景切换且保留完整快照及预设", b =>
        {
            var scene = DifferentScene(f);
            var actual = f.Get<DesktopSnapshot>("snapshot");
            var extra = Enumerable.Range(1, 4).Select(i => actual.Displays[0] with { Id = "EXTRA" + i,
                DeviceName = "EXTRA" + i, Connected = true, Enabled = false, Primary = false });
            b.Service.Live = actual with { Displays = actual.Displays.Concat(extra).ToList() };
            var library = Json(f.Profiles);
            Click(SceneButton(f, scene.Id));
            Check(f.Get<DesktopSnapshot>("snapshot").Displays.Count == 7, "超过上限时丢弃了检测结果");
            Check(f.Get<TextBlock>("StatusLabel").Text.Contains("6"), "未解释六屏限制");
            Check(Json(f.Profiles) == library && !File.Exists(b.ProfilePath), "过限检查改写了预设库");
            b.NoSwitch();
        });

        Case("辅助功能 Toggle 与开关外观同步更新启停草稿", b =>
        {
            var before = Json(f.Get<DesktopSnapshot>("snapshot"));
            f.SelectMonitor(FakeDisplayService.Left);
            var toggle = f.Get<CheckBox>("EnabledToggle");
            var peer = new System.Windows.Automation.Peers.CheckBoxAutomationPeer(toggle);
            var provider = (System.Windows.Automation.Provider.IToggleProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle)!;
            provider.Toggle(); Check(!f.Left.Enabled && toggle.IsChecked == false, "辅助功能关闭仅改了外观");
            provider.Toggle(); Check(f.Left.Enabled && toggle.IsChecked == true, "辅助功能重新启用未修改草稿");
            Check(Json(f.Get<DesktopSnapshot>("snapshot")) == before, "开关草稿提前改变实测状态");
            b.NoSwitch();
        });

        Case("新建、修改、保存仅写预设库，不读取或切换显示器", b =>
        {
            var before = Json(f.Get<DesktopSnapshot>("snapshot"));
            var originalLibrary = Json(f.Profiles); var count = f.Profiles.Count;
            Click(f.Get<Button>("NewPresetButton"));
            Check(!string.IsNullOrWhiteSpace(f.Get<TextBox>("PresetNameBox").Text) && !f.Profiles.Any(p => p.Name == f.Get<TextBox>("PresetNameBox").Text), "默认名称为空或重复");
            var id = f.Get<string>("editingPresetId");
            Check(f.Get<bool>("creatingPreset") && !f.Profiles.Any(p => p.Id == id), "新建没有独立草稿身份");
            Check(Json(f.Profiles) == originalLibrary && !File.Exists(b.ProfilePath), "进入新建就持久化或修改了预设库");
            Check(f.Get<Button>("ApplyButton").Visibility == Visibility.Collapsed, "新建预设仍显示硬件应用按钮");
            f.Get<TextBox>("PresetNameBox").Text = "  工作区新预设  ";
            var draftBeforeRotation = Json(f.Draft);
            f.Get<ComboBox>("RotationBox").SelectedIndex = (f.Get<ComboBox>("RotationBox").SelectedIndex + 1) % 4;
            Check(Json(f.Draft) != draftBeforeRotation, "检查器修改没有到达新建草稿");
            var expected = f.Draft.Displays.ToArray();
            b.NoSwitch();
            Click(f.Get<Button>("SavePresetButton"));
            var saved = f.Profiles.Single(p => p.Id == id);
            Check(f.Profiles.Count == count + 1 && saved.Name == "工作区新预设", "保存没有仅新增一个已命名预设");
            Check(saved.Displays.SequenceEqual(expected), "保存内容与编辑草稿不一致");
            Check(b.Store.Load().Single(p => p.Id == id).Displays.SequenceEqual(expected), "落盘布局与草稿不一致");
            Check(Json(f.Get<DesktopSnapshot>("snapshot")) == before, "新建或保存改变了实测快照");
            Check(f.Get<string?>("editingPresetId") == null && !f.Get<bool>("dirty"), "保存后没有退出预设编辑");
            b.NoSwitch();
        });

        Case("统一管理中的编辑不速切，保存同 ID 更新而非追加", b =>
        {
            var original = DifferentScene(f); var originalTargets = original.Displays.ToArray();
            var otherPresets = Json(f.Profiles.Where(p => p.Id != original.Id));
            var count = f.Profiles.Count; var before = Json(f.Get<DesktopSnapshot>("snapshot"));
            var oldName = original.Name;
            Click(EditButton(f, original.Id));
            Check(f.Get<string>("editingPresetId") == original.Id && !f.Get<bool>("creatingPreset"), "编辑没有绑定原预设 ID");
            Check(!ReferenceEquals(f.Draft.Displays, original.Displays), "编辑草稿与已存布局共享可变列表");
            Check(f.Get<Button>("ApplyButton").Visibility == Visibility.Collapsed, "编辑预设仍显示应用布局按钮");
            Check(f.Get<Button>("SaveAsButton").Visibility == Visibility.Collapsed
                && f.Get<Button>("SavePresetButton").Visibility == Visibility.Visible
                && Equals(f.Get<Button>("CancelEditButton").Content, "取消"), "编辑状态混入了应用或第二种保存");
            Check(!f.Get<Panel>("WorkspaceActions").Children.OfType<Button>().Any(x => x.Content?.ToString()?.Contains("删除") == true), "编辑侧栏仍有删除入口");
            f.Get<TextBox>("PresetNameBox").Text = "已修改的同一预设";
            f.Get<ComboBox>("RotationBox").SelectedIndex = (f.Get<ComboBox>("RotationBox").SelectedIndex + 1) % 4;
            var expected = f.Draft.Displays.ToArray();
            Check(original.Name == oldName && original.Displays.SequenceEqual(originalTargets), "保存前改写了已存预设");
            b.NoSwitch();
            Click(f.Get<Button>("SavePresetButton"));
            Check(f.Profiles.Count == count && f.Profiles.Count(p => p.Id == original.Id) == 1, "编辑保存追加了预设或改变了 ID");
            var saved = f.Profiles.Single(p => p.Id == original.Id);
            Check(saved.Name == "已修改的同一预设" && saved.Displays.SequenceEqual(expected), "同 ID 更新遗漏名称或布局");
            Check(Json(f.Profiles.Where(p => p.Id != original.Id)) == otherPresets, "编辑保存改变了其他预设");
            var persisted = b.Store.Load();
            Check(persisted.Count == count && persisted.Single(p => p.Id == original.Id).Displays.SequenceEqual(expected), "持久化仍追加或保存了旧布局");
            Check(Json(f.Get<DesktopSnapshot>("snapshot")) == before, "编辑保存改变了当前快照");
            b.NoSwitch();
        });

        Case("dirty 当前布局显示工作区内 Apply，保存为预设后隐藏", b =>
        {
            f.Invoke("RenderCanvas");
            var apply = f.Get<Button>("ApplyButton");
            Check(apply.Visibility == Visibility.Visible && !apply.IsEnabled, "应用按钮必须常驻且无修改时置灰");
            var before = Json(f.Get<DesktopSnapshot>("snapshot"));
            f.Get<ComboBox>("RotationBox").SelectedIndex = 1;
            Check(f.Get<bool>("dirty") && apply.Visibility == Visibility.Visible && apply.IsEnabled, "当前布局修改后没有可用 Apply");
            Check(f.Get<Panel>("WorkspaceActions").Children.Contains(apply), "Apply 不在工作区操作区内");
            var edited = f.Draft.Displays.ToArray();
            Click(f.Get<Button>("SaveAsButton"));
            Check(f.Get<bool>("creatingPreset") && f.Draft.Displays.SequenceEqual(edited), "保存为预设丢失当前编辑");
            Check(apply.Visibility == Visibility.Collapsed && f.Get<Button>("SavePresetButton").Visibility == Visibility.Visible,
                "进入预设编辑后没有从 Apply 切换为保存");
            Check(!File.Exists(b.ProfilePath) && Json(f.Get<DesktopSnapshot>("snapshot")) == before, "保存为预设入口提前写盘或改变显示快照");
            b.NoSwitch();
        });

        Case("场景按钮一次点击直接到达记录切换边界，传递独立完整请求", b =>
        {
            var scene = DifferentScene(f); var expected = scene.Displays.ToArray();
            var library = Json(f.Profiles); var before = Json(f.Get<DesktopSnapshot>("snapshot"));
            Click(SceneButton(f, scene.Id));
            Check(b.Switches.Count == 1, "场景点击没有恰好一次到达 switchLayout");
            var submitted = b.Switches.Single();
            Check(submitted.Id == scene.Id && submitted.Name == scene.Name && submitted.Displays.SequenceEqual(expected), "速切请求不是点击的完整场景");
            Check(!ReferenceEquals(submitted.Displays, scene.Displays), "切换请求共享预设库可变列表");
            Check(f.Get<string?>("editingPresetId") == null && !f.Get<bool>("dirty"), "场景点击退化为等待 Apply 的预览");
            Check(Json(f.Profiles) == library && !File.Exists(b.ProfilePath), "场景点击修改或持久化了预设库");
            Check(Json(f.Get<DesktopSnapshot>("snapshot")) == before, "记录适配器没有回读却改写了实测快照");
            submitted.Displays.Clear();
            Check(scene.Displays.SequenceEqual(expected), "切换边界修改请求污染了预设库");
        });

        Case("已是当前场景时按钮 no-op，不到达切换边界", b =>
        {
            var snapshot = f.Get<DesktopSnapshot>("snapshot");
            var current = new DisplayProfile("workflow-current", "当前实际布局", snapshot.Displays.Select(Target).ToList());
            f.Profiles.Add(current);
            var before = Json(snapshot); var library = Json(f.Profiles);
            Click(SceneButton(f, current.Id));
            Check(f.Get<TextBlock>("StatusLabel").Text.Contains("无需重复切换"), "当前场景缺少 no-op 反馈");
            Check(Json(f.Get<DesktopSnapshot>("snapshot")) == before && Json(f.Profiles) == library && !File.Exists(b.ProfilePath), "no-op 产生了状态或持久化副作用");
            b.NoSwitch();
        });

        foreach (var missing in new[] { "absent", "disconnected" })
            Case("场景所需显示器 " + missing + " 时拒绝切换", b =>
            {
                var scene = DifferentScene(f);
                var unavailable = scene.Displays.First(d => d.Enabled).Id;
                var snapshot = f.Get<DesktopSnapshot>("snapshot");
                f.Set("snapshot", snapshot with { Displays = missing == "absent"
                    ? snapshot.Displays.Where(d => d.Id != unavailable).ToList()
                    : snapshot.Displays.Select(d => d.Id == unavailable ? d with { Connected = false } : d).ToList() });
                var before = Json(f.Get<DesktopSnapshot>("snapshot")); var library = Json(f.Profiles);
                b.Service.Live = f.Get<DesktopSnapshot>("snapshot");
                Click(SceneButton(f, scene.Id));
                Check(f.Get<TextBlock>("StatusLabel").Text.Contains("未连接"), "缺屏场景没有拒绝反馈");
                Check(Json(f.Get<DesktopSnapshot>("snapshot")) == before && Json(f.Profiles) == library && !File.Exists(b.ProfilePath), "缺屏拒绝改变了快照或预设库");
                b.NoSwitch();
            });

        Case("缓存A实测B：点击A必须采集并到达切换边界", b =>
        {
            var cached = f.Get<DesktopSnapshot>("snapshot");
            var scene = new DisplayProfile("stale-current", "缓存场景A", cached.Displays.Select(Target).ToList());
            f.Profiles.Add(scene);
            b.Service.Live = cached with { Displays = cached.Displays.Select(d => d.Primary ? d with { RefreshRate = 180 } : d).ToList() };
            Click(SceneButton(f, scene.Id));
            Check(b.Switches.Count == 1, "缓存A误判no-op：实测已变为B，点击A未切换");
            Check(b.Service.Captures == 1, "场景提前判定之前没有恰好一次live Capture");
        });

        foreach (var missing in new[] { "absent", "disconnected" })
            Case("过期missing " + missing + "：实测已连接应切换", b =>
            {
                var scene = DifferentScene(f);
                var id = scene.Displays.First(d => d.Enabled).Id;
                var cached = f.Get<DesktopSnapshot>("snapshot");
                f.Set("snapshot", cached with { Displays = missing == "absent"
                    ? cached.Displays.Where(d => d.Id != id).ToList()
                    : cached.Displays.Select(d => d.Id == id ? d with { Connected = false } : d).ToList() });
                Click(SceneButton(f, scene.Id));
                Check(b.Switches.Count == 1, "缓存missing误拦截已连接显示器");
                Check(b.Service.Captures == 1, "missing判定未采集实测状态");
            });

        Case("缓存不同但实测已经是目标：实际no-op保护", b =>
        {
            var scene = DifferentScene(f);
            b.Service.Live = LiveFor(scene);
            Click(SceneButton(f, scene.Id));
            b.NoSwitch();
            Check(b.Service.Captures == 1 && f.Get<TextBlock>("StatusLabel").Text.Contains("无需重复切换"), "没有按实测布局no-op");
        });

        Case("缓存当前但实测缺屏：实际missing保护", b =>
        {
            var cached = f.Get<DesktopSnapshot>("snapshot");
            var scene = new DisplayProfile("live-missing", "实测缺屏", cached.Displays.Select(Target).ToList());
            f.Profiles.Add(scene);
            b.Service.Live = cached with { Displays = cached.Displays.Select(d => d.Primary ? d with { Connected = false } : d).ToList() };
            Click(SceneButton(f, scene.Id));
            b.NoSwitch();
            Check(b.Service.Captures == 1 && f.Get<TextBlock>("StatusLabel").Text.Contains("未连接"), "缓存no-op覆盖了实测缺屏保护");
        });

        Case("Capture失败：不切屏且释放busy并允许重试", b =>
        {
            var scene = DifferentScene(f);
            var before = Json(f.Draft);
            b.Service.OnCapture = () => throw new InvalidOperationException("FAKE_CAPTURE_FAILED");
            Click(SceneButton(f, scene.Id));
            b.NoSwitch();
            Check(b.Service.Captures == 1 && f.Get<TextBlock>("StatusLabel").Text.Contains("FAKE_CAPTURE_FAILED"), "Capture失败未解释拒绝");
            Check(!f.Get<bool>("busy") && Json(f.Draft) == before, "Capture失败残留busy或丢失草稿");
            b.Service.OnCapture = null;
            Click(SceneButton(f, scene.Id));
            Check(b.Switches.Count == 1 && b.Service.Captures == 2, "Capture失败后无法重试");
        });

        foreach (bool save in new[] { false, true })
            Case("编辑期间pending保留，正常" + (save ? "保存" : "取消") + "后补刷新", b =>
            {
                Click(EditButton(f, DifferentScene(f).Id));
                if (save) f.Get<TextBox>("PresetNameBox").Text = "pending保存";
                var draft = Json(f.Draft);
                var signal = DateTime.UtcNow.AddSeconds(-10);
                f.Set("lastSignal", signal);
                f.RaiseHardwareTick();
                Check(f.Get<DateTime>("lastSignal") == signal, "编辑期间定时器丢失pending refresh");
                Check(b.Service.Captures == 0 && Json(f.Draft) == draft, "定时器采集或覆盖了编辑草稿");
                b.Service.Live = LiveFor(f.Profiles.First(p => p.Displays.Count(d => d.Enabled) == 1));
                Click(f.Get<Button>(save ? "SavePresetButton" : "CancelEditButton"));
                Check(b.Service.Captures == 1 && Json(f.Get<DesktopSnapshot>("snapshot")) == Json(b.Service.Live), "退出编辑没有立即补刷新");
                Check(f.Get<DateTime>("lastSignal") == DateTime.MinValue && !f.Get<bool>("busy"), "成功刷新未消费pending或未释放busy");
                f.RaiseHardwareTick();
                Check(b.Service.Captures == 1, "已消费通知仍重复刷新");
                b.NoSwitch();
            });

        Case("dirty布局定时器保留pending及草稿", b =>
        {
            f.Get<ComboBox>("RotationBox").SelectedIndex = 1;
            var before = Json(f.Draft);
            var signal = DateTime.UtcNow.AddSeconds(-10);
            f.Set("lastSignal", signal);
            f.RaiseHardwareTick();
            Check(f.Get<DateTime>("lastSignal") == signal && b.Service.Captures == 0, "dirty状态丢失pending或采集");
            Check(Json(f.Draft) == before && f.Get<bool>("dirty"), "dirty草稿被覆盖");
            b.NoSwitch();
        });

        Case("busy期间定时器不消费pending", b =>
        {
            var signal = DateTime.UtcNow.AddSeconds(-10);
            f.Set("lastSignal", signal); f.Set("busy", true);
            f.RaiseHardwareTick();
            Check(f.Get<DateTime>("lastSignal") == signal && b.Service.Captures == 0, "busy事件消费pending或重入采集");
            f.Set("busy", false);
            b.NoSwitch();
        });

        Case("采集等待期间拒绝点击和timer重入，保留新通知", b =>
        {
            var button = SceneButton(f, DifferentScene(f).Id);
            using var release = new ManualResetEventSlim();
            b.Service.OnCapture = () =>
            {
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("假Capture未释放");
                return b.Service.Live;
            };
            f.Set("lastSignal", DateTime.UtcNow.AddSeconds(-20));
            var newerSignal = DateTime.UtcNow.AddSeconds(-10);
            try
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
                Check(f.Get<bool>("busy"), "首次await之前没有锁定busy");
                f.Set("lastSignal", newerSignal);
                f.RaiseHardwareTick(complete: false);
                // Exercise the guard even with an event from a stale/disabled card.
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
                Check(b.Switches.Count == 0 && f.Get<DateTime>("lastSignal") == newerSignal, "采集中重入切换或消费了新通知");
            }
            finally
            {
                release.Set();
                ((WorkflowCallbacks)SynchronizationContext.Current!).Complete();
            }
            Check(b.Service.Captures == 1 && b.Switches.Count == 1 && !f.Get<bool>("busy"), "重复点击引发重复Capture/切换或残留busy");
            Check(f.Get<DateTime>("lastSignal") == newerSignal, "完成旧采集时丢失了新通知");
            b.Service.OnCapture = null;
            f.RaiseHardwareTick();
            Check(b.Service.Captures == 2 && f.Get<DateTime>("lastSignal") == DateTime.MinValue, "新通知没有独立补刷新");
        });

        Case("pending采集失败保留通知，下次tick可重试", b =>
        {
            var signal = DateTime.UtcNow.AddSeconds(-10);
            f.Set("lastSignal", signal);
            b.Service.OnCapture = () => throw new InvalidOperationException("FAKE_PENDING_CAPTURE_FAILED");
            f.RaiseHardwareTick();
            Check(b.Service.Captures == 1 && f.Get<DateTime>("lastSignal") == signal && !f.Get<bool>("busy"), "失败吞掉pending或残留busy");
            b.Service.OnCapture = null;
            f.RaiseHardwareTick();
            Check(b.Service.Captures == 2 && f.Get<DateTime>("lastSignal") == DateTime.MinValue, "pending失败不能重试");
            b.NoSwitch();
        });

        Case("recoveryBlocked 场景按钮拒绝且保留草稿", b =>
        {
            var button = SceneButton(f, DifferentScene(f).Id);
            f.Set("recoveryBlocked", true);
            var before = Json(f.Draft); var library = Json(f.Profiles);
            Click(button);
            Check(f.Get<TextBlock>("StatusLabel").Text.Contains("恢复状态未确认"), "场景未解释恢复锁定");
            Check(Json(f.Draft) == before && Json(f.Profiles) == library && !File.Exists(b.ProfilePath), "恢复锁定仍改变草稿或预设");
            b.NoSwitch();
        });

        Case("recoveryBlocked 工作区 Apply 事件在硬件边界前拒绝", b =>
        {
            f.Get<ComboBox>("RotationBox").SelectedIndex = 1;
            f.Set("recoveryBlocked", true);
            var before = Json(f.Draft);
            // Exercise the handler even if UI enablement has not yet been refreshed.
            f.Get<Button>("ApplyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(f.Get<TextBlock>("StatusLabel").Text.Contains("恢复状态未确认") && !f.Get<bool>("busy"), "Apply 没有同步拒绝恢复锁定");
            Check(Json(f.Draft) == before && !File.Exists(b.ProfilePath), "被拒绝 Apply 改变草稿或写盘");
            b.NoSwitch();
        });
    }

    private static DisplayProfile DifferentScene(WindowFixture f) => f.Profiles.First(p =>
        !DisplayPresentation.SameLayout(p.Displays, f.Get<DesktopSnapshot>("snapshot").Displays.Select(Target)));
    private static Button SceneButton(WindowFixture f, string id)
    {
        f.Invoke("RenderScenes");
        return f.Get<StackPanel>("SceneStrip").Children.OfType<Button>()
            .Single(b => AutomationProperties.GetAutomationId(b) == "Scene:" + id);
    }
    private static Button EditButton(WindowFixture f, string id)
    {
        Click(f.Get<Button>("ManagePresetsButton"));
        return SceneButton(f, id);
    }
    private static void Click(Button button)
    {
        Check(button.IsEnabled && button.Visibility == Visibility.Visible, "测试入口按钮不可用：" + button.Content);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        (SynchronizationContext.Current as WorkflowCallbacks)?.Complete();
    }
    private static DesktopSnapshot LiveFor(DisplayProfile profile) => FakeDisplayService.Snapshot() with
    {
        Displays = FakeDisplayService.Snapshot().Displays.Select(d =>
        {
            var t = profile.Displays.Single(t => t.Id == d.Id);
            return d with { Enabled = t.Enabled, Primary = t.Primary, X = t.X, Y = t.Y,
                Width = t.Width, Height = t.Height, Rotation = t.Rotation, RefreshRate = t.RefreshRate };
        }).ToList()
    };
    private static DisplayTarget Target(DisplayInfo d) => new(d.Id, d.Enabled, d.Primary, d.X, d.Y, d.Width, d.Height, d.Rotation, d.RefreshRate);
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private sealed class Boundary : IDisposable
    {
        private readonly WindowFixture fixture;
        private readonly ProfileStore originalStore;
        private readonly IDisplayService originalService;
        private readonly Func<DisplayProfile, Task>? originalSwitch;
        private readonly string directory = Path.Combine(Path.GetTempPath(), "PingXu.WorkspaceWorkflow." + Guid.NewGuid().ToString("N"));
        private readonly int windows = Application.Current.Windows.Count;
        internal ProfileStore Store { get; }
        internal string ProfilePath => Path.Combine(directory, "profiles.json");
        internal List<DisplayProfile> Switches { get; } = [];
        internal WorkflowDisplayService Service { get; } = new();
        internal Boundary(WindowFixture f)
        {
            fixture = f; originalStore = f.Get<ProfileStore>("store");
            originalService = f.Get<IDisplayService>("service");
            f.Set("service", Service);
            originalSwitch = f.Get<Func<DisplayProfile, Task>?>("switchLayout");
            Store = new ProfileStore(directory);
            f.Set("store", Store);
            f.Set("switchLayout", (Func<DisplayProfile, Task>)(profile => { Switches.Add(profile); return Task.CompletedTask; }));
        }
        internal void NoSwitch()
        {
            Check(Switches.Count == 0, "编辑/拒绝/no-op 意外到达场景切换边界");
            AssertIsolated();
        }
        internal void AssertIsolated()
        {
            fixture.AssertIsolated();
            Check(Service.UnexpectedCalls == 0, "意外到达Validate/Apply/Restore");
            Check(Application.Current.Windows.Count == windows, "按钮意外创建了对话窗口");
        }
        public void Dispose()
        {
            fixture.Set("store", originalStore); fixture.Set("switchLayout", originalSwitch);
            fixture.Set("service", originalService);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
