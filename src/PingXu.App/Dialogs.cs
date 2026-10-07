using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Documents;
using PingXu.Core;
using Microsoft.Win32;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using RadioButton = System.Windows.Controls.RadioButton;
using ComboBox = System.Windows.Controls.ComboBox;
using Panel = System.Windows.Controls.Panel;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = System.Windows.MessageBox;
namespace PingXu.App;
public static class Dialogs
{
    public static bool ConfirmDiscard(Window owner)
    {
        var window = Shell(owner, "放弃修改？", 460, 240);
        window.WindowStyle = WindowStyle.None;
        var body = new StackPanel { Margin = new(28) };
        body.Children.Add(Paragraph("放弃修改？", 25));
        body.Children.Add(Paragraph("这次修改尚未保存。继续将丢弃修改，已保存的预设不受影响。", 14, new(0,16,0,24)));
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var stay = new Button { Content = "继续编辑", IsCancel = true, IsDefault = true, Padding = new(18,10,18,10), Margin = new(0,0,12,0) };
        var discard = new Button { Content = "放弃修改", Padding = new(18,10,18,10) };
        discard.SetResourceReference(FrameworkElement.StyleProperty, "Primary");
        discard.Click += (_, _) => window.DialogResult = true;
        row.Children.Add(stay); row.Children.Add(discard); body.Children.Add(row);
        var border = new Border { Child = body, BorderThickness = new(1) };
        border.SetResourceReference(Border.BorderBrushProperty, "Line");
        border.SetResourceReference(Border.BackgroundProperty, "Panel");
        window.Content = border;
        return window.ShowDialog() == true;
    }
    public static void ShowFailure(Window owner, string title, string explanation, string outcome, string nextStep, string details)
    {
        var window = Shell(owner, title, 570, 440);
        window.ResizeMode = ResizeMode.CanResize; window.MinWidth = 360; window.MinHeight = 300;
        window.Content = CreateFailureContent(title, explanation, outcome, nextStep, details, window.Close);
        window.ShowDialog();
    }

    // Testable content factory: no display calls, file writes or clipboard access.
    public static FrameworkElement CreateFailureContent(string title, string explanation, string outcome,
        string nextStep, string details, Action close)
    {
        var body = new StackPanel { Margin = new(24) };
        body.Children.Add(Paragraph(title, 24));
        var reason = Paragraph(explanation, 16, new(0, 18, 0, 12));
        Identify(reason, "FailureExplanation", "发生了什么"); body.Children.Add(reason);
        var state = Paragraph(outcome, 14, new(0, 0, 0, 12));
        Identify(state, "FailureOutcome", "当前布局状态"); body.Children.Add(state);
        body.Children.Add(Paragraph(nextStep, 14, new(0, 0, 0, 18)));
        var detailText = new TextBox { Text = details, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true, MinHeight = 70, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Identify(detailText, "FailureDetailsText", "错误详情，可选中复制");
        var detailPanel = new StackPanel();
        detailPanel.Children.Add(Paragraph("以下内容可选中复制，反馈问题时请一并提供。", 12, new(0, 6, 0, 8)));
        detailPanel.Children.Add(detailText);
        var expand = new Expander { Header = "错误详情", Content = detailPanel, IsExpanded = false };
        Identify(expand, "FailureDetails", "展开错误详情"); body.Children.Add(expand);
        var done = new Button { Content = "知道了", IsDefault = true, IsCancel = true, MinWidth = 110,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 20, 0, 0) };
        done.SetResourceReference(FrameworkElement.StyleProperty, "Primary");
        Identify(done, "FailureClose", "知道了"); done.Click += (_, _) => close(); body.Children.Add(done);
        return ScrollBody(body);
    }
    static Window Shell(Window owner, string title, double width = 480, double height = 310) => new() { Owner = owner, Title = title, Width = width, Height = height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false };
    public static string? Ask(Window owner, string title, string prompt, string value)
    {
        var w = Shell(owner, title); var body = new StackPanel { Margin = new(26) }; body.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold }); body.Children.Add(new TextBlock { Text = prompt, Margin = new(0, 10, 0, 18) }); var input = new TextBox { Text = value, MaxLength = 60 }; body.Children.Add(input); var b = new Button { Content = "保存", Margin = new(0, 20, 0, 0), Style = (Style)owner.FindResource("Primary"), IsDefault = true }; b.Click += (_, _) => { if (string.IsNullOrWhiteSpace(input.Text)) return; w.DialogResult = true; }; body.Children.Add(b); w.Content = body; w.Loaded += (_, _) => { input.Focus(); input.SelectAll(); }; return w.ShowDialog() == true ? input.Text.Trim() : null;
    }
    public static Window ConfirmDisplay(Window owner, Action keep, Action cancel, out TextBlock countdown)
        => ConfirmDisplay(owner, _ => keep(), cancel, out countdown, 20, false);

    public static Window ConfirmDisplay(Window owner, Action<bool> keep, Action cancel, out TextBlock countdown,
        int timeoutSeconds, bool canRemember)
        => ConfirmDisplay(owner, (remember, _) => keep(remember), cancel, out countdown, timeoutSeconds, canRemember);

    public static Window ConfirmDisplay(Window owner, Action<bool, bool> keep, Action cancel, out TextBlock countdown,
        int timeoutSeconds, bool canRemember)
    {
        var w = Shell(owner, "确认显示布局", 550, 520);
        w.Topmost = true; w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.ResizeMode = ResizeMode.CanResize; w.MinWidth = 340; w.MinHeight = 260;
        w.Content = CreateConfirmDisplayContent(keep, cancel, out countdown, timeoutSeconds, canRemember,
            out var cancelIfUndecided, w.Close);
        w.Closing += (_, _) => cancelIfUndecided();
        return w;
    }

    // Pure control factory: no Window, timer, persistence or native display calls.
    public static FrameworkElement CreateConfirmDisplayContent(Action<bool> keep, Action cancel, out TextBlock countdown,
        int timeoutSeconds, bool canRemember, out Action cancelIfUndecided, Action? close = null)
        => CreateConfirmDisplayContent((remember, _) => keep(remember), cancel, out countdown, timeoutSeconds,
            canRemember, out cancelIfUndecided, close);

    public static FrameworkElement CreateConfirmDisplayContent(Action<bool, bool> keep, Action cancel, out TextBlock countdown,
        int timeoutSeconds, bool canRemember, out Action cancelIfUndecided, Action? close = null)
    {
        new ConfirmationOptions(ConfirmationMode.Always, timeoutSeconds).Validate();
        var body = new StackPanel { Margin = new(24) };
        body.Children.Add(Paragraph("所有屏幕显示正常吗？", 25));
        body.Children.Add(Paragraph("请检查屏幕方向、主屏和鼠标跨屏位置。", margin: new(0, 12, 0, 16)));
        countdown = Paragraph($"{timeoutSeconds} 秒后自动恢复", 19);
        countdown.SetResourceReference(TextBlock.ForegroundProperty, "Acid");
        Identify(countdown, "ConfirmationCountdown", "自动恢复倒计时"); body.Children.Add(countdown);
        var remember = new CheckBox { Content = Paragraph("记住此预设，下次直接切换"), IsChecked = canRemember,
            Visibility = canRemember ? Visibility.Visible : Visibility.Collapsed, Margin = new(0, 18, 0, 0), Padding = new(0, 2, 0, 2) };
        Identify(remember, "RememberProfile", "记住此预设，下次直接切换"); body.Children.Add(remember);
        var dontAskAgain = new CheckBox { Content = Paragraph("以后不再询问，直接切换"), IsChecked = false,
            Margin = new(0, 12, 0, 0), Padding = new(0, 2, 0, 2) };
        Identify(dontAskAgain, "DontAskAgain", "以后不再询问，直接切换"); body.Children.Add(dontAskAgain);
        var risk = Paragraph("关闭后，画面异常也不会因等待超时而自动恢复。程序发现切换失败时仍会尝试恢复原布局。", 12, new(0, 6, 0, 0));
        risk.SetResourceReference(TextBlock.ForegroundProperty, "Acid");
        Identify(risk, "DontAskAgainRisk", "不再询问的风险说明"); body.Children.Add(risk);
        var status = Paragraph("", margin: new(0, 12, 0, 0));
        Identify(status, "ConfirmationStatus", "确认结果"); body.Children.Add(status);
        var row = new WrapPanel { Margin = new(0, 18, 0, 0) };
        var revert = new Button { Content = "恢复原布局", MinWidth = 140, Margin = new(0, 0, 12, 8), IsCancel = true };
        var confirm = new Button { Content = "保留此布局", MinWidth = 140, Margin = new(0, 0, 0, 8) };
        confirm.SetResourceReference(FrameworkElement.StyleProperty, "Primary");
        Identify(revert, "RevertDisplay", "恢复原布局"); Identify(confirm, "KeepDisplay", "保留此布局");
        bool decided = false, deciding = false;
        void SetEnabled(bool enabled)
        {
            revert.IsEnabled = confirm.IsEnabled = dontAskAgain.IsEnabled = enabled;
            remember.IsEnabled = enabled && canRemember && dontAskAgain.IsChecked != true;
        }
        dontAskAgain.Checked += (_, _) => SetEnabled(!decided && !deciding);
        dontAskAgain.Unchecked += (_, _) => SetEnabled(!decided && !deciding);
        SetEnabled(true);
        void CancelIfUndecided()
        {
            if (decided || deciding) return;
            deciding = true; SetEnabled(false);
            try { cancel(); decided = true; }
            catch (Exception ex) { status.Text = "请求恢复未完成：" + ex.Message; }
            finally { deciding = false; SetEnabled(!decided); }
        }
        cancelIfUndecided = CancelIfUndecided;
        revert.Click += (_, _) => { if (decided || deciding) return; CancelIfUndecided(); if (decided) close?.Invoke(); };
        confirm.Click += (_, _) =>
        {
            if (decided || deciding) return;
            deciding = true; SetEnabled(false);
            try
            {
                bool dontAsk = dontAskAgain.IsChecked == true;
                keep(canRemember && !dontAsk && remember.IsChecked == true, dontAsk);
                decided = true; status.Text = "正在保存确认的布局…";
            }
            catch (Exception ex) { status.Text = "保留未完成：" + ex.Message; }
            finally { deciding = false; SetEnabled(!decided); }
        };
        row.Children.Add(revert); row.Children.Add(confirm); body.Children.Add(row);
        return ScrollBody(body);
    }
    public static void Manage(Window owner, List<DisplayProfile> sourceProfiles, Action<List<DisplayProfile>> save,
        Action? createPreset = null, Action<DisplayProfile>? editPreset = null)
    {
        var w = Shell(owner, "预设管理", 680, 620);
        w.ResizeMode = ResizeMode.CanResize; w.MinWidth = 360; w.MinHeight = 300;
        Identify(w, "SceneManager.Window", "预设管理");
        Action? next = null;
        // Queue the editor until the modal message loop has actually returned and its owner is enabled.
        w.Content = CreateSceneManagerContent(sourceProfiles, save,
            createPreset == null ? null : () => next = createPreset,
            editPreset == null ? null : p => next = () => editPreset(p),
            w.Close,
            () =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "屏搭预设 (*.json)|*.json" };
                if (dialog.ShowDialog(w) != true) return null;
                if (new FileInfo(dialog.FileName).Length > 2_000_000) throw new ArgumentException("文件过大。");
                return File.ReadAllText(dialog.FileName);
            },
            json =>
            {
                var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "屏搭预设 (*.json)|*.json", FileName = "屏搭预设.json" };
                if (dialog.ShowDialog(w) != true) return false;
                ProfileStore.AtomicWrite(dialog.FileName, json); return true;
            });
        w.ShowDialog();
        try { next?.Invoke(); }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "打开预设编辑失败：" + ex.Message + "\n原有预设未改变。",
                "预设管理", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Offscreen scene manager: construction has no Window, file dialog, persistence or hardware effects.
    /// Save receives an isolated snapshot and must throw on failure; the displayed library changes only on success.
    /// New/edit close first, then invoke the supplied callback. Null callbacks disable their actions.
    /// Import returns JSON or null on cancellation; export returns false on cancellation.
    /// </summary>
    public static FrameworkElement CreateSceneManagerContent(IReadOnlyList<DisplayProfile> sourceProfiles,
        Action<List<DisplayProfile>> save, Action? createPreset = null, Action<DisplayProfile>? editPreset = null,
        Action? close = null, Func<string?>? importJson = null, Func<string, bool>? exportJson = null)
    {
        ArgumentNullException.ThrowIfNull(sourceProfiles);
        ArgumentNullException.ThrowIfNull(save);
        static DisplayProfile CopyProfile(DisplayProfile p) => p with { Displays = p.Displays.ToList() };
        static List<DisplayProfile> Snapshot(IEnumerable<DisplayProfile> items) => items.Select(CopyProfile).ToList();
        var profiles = Snapshot(sourceProfiles);
        bool working = false, leaving = false;
        var body = new StackPanel { Margin = new(24) };
        var root = ScrollBody(body); Identify(root, "SceneManager", "场景库管理");
        var header = new DockPanel { Margin = new(0, 0, 0, 8) };
        var create = ActionButton("新建预设", "SceneManager.New");
        create.Margin = new(12, 0, 0, 0); create.IsEnabled = createPreset != null;
        create.SetResourceReference(FrameworkElement.StyleProperty, "Primary");
        DockPanel.SetDock(create, Dock.Right); header.Children.Add(create);
        header.Children.Add(new TextBlock { Text = "预设管理", FontSize = 25, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center });
        body.Children.Add(header);
        var count = Paragraph("", 12, new(0, 0, 0, 14));
        count.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Identify(count, "SceneManager.Count", "已保存预设数量"); body.Children.Add(count);
        var status = Paragraph("", 13, new(0, 0, 0, 12));
        status.Visibility = Visibility.Collapsed;
        Identify(status, "SceneManager.Status", "场景库操作结果");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        body.Children.Add(status);
        var list = new StackPanel(); Identify(list, "SceneManager.List", "我的预设"); body.Children.Add(list);
        var backup = new Expander { Header = "备份与迁移", IsExpanded = false, Margin = new(0, 18, 0, 0) };
        Identify(backup, "SceneManager.Backup", "备份与迁移");
        var backupBody = new StackPanel { Margin = new(0, 12, 0, 0) };
        backupBody.Children.Add(Paragraph("导入会追加到场景库，保留已有预设。", 12));
        var transfer = new WrapPanel { Margin = new(0, 10, 0, 0) };
        var import = ActionButton("导入预设", "SceneManager.Import"); import.IsEnabled = importJson != null;
        var export = ActionButton("导出预设", "SceneManager.Export"); export.IsEnabled = exportJson != null;
        transfer.Children.Add(import); transfer.Children.Add(export); backupBody.Children.Add(transfer);
        backup.Content = backupBody; body.Children.Add(backup);

        void Report(string message, bool error = false)
        {
            status.Text = message; status.Visibility = Visibility.Visible;
            status.SetResourceReference(TextBlock.ForegroundProperty, error ? "Acid" : "Muted");
            status.BringIntoView();
        }
        void Run(string operation, Action action)
        {
            if (working || leaving) return;
            working = true; body.IsEnabled = false;
            try { action(); }
            catch (Exception ex) { Report(operation + "失败：" + ex.Message, true); }
            finally { working = false; body.IsEnabled = !leaving; }
        }
        void Commit(List<DisplayProfile> candidate, string message)
        {
            if (candidate.Count > 100) throw new ArgumentException("最多保存100个预设。");
            var accepted = Snapshot(candidate);
            // Even a callback that mutates its argument then throws cannot damage this library or the source.
            save(Snapshot(accepted));
            profiles = accepted; Refresh(); Report(message);
        }
        void Leave(Action? action)
        {
            if (action == null) return;
            Run("打开预设编辑", () =>
            {
                close?.Invoke();
                leaving = true;
                try { action(); }
                catch { leaving = false; throw; }
            });
        }
        string CopyName(string name)
        {
            for (int n = 1; ; n++)
            {
                string suffix = n == 1 ? " · 副本" : $" · 副本 {n}";
                string candidate = name[..Math.Min(name.Length, 60 - suffix.Length)].TrimEnd() + suffix;
                if (profiles.All(p => !StringComparer.OrdinalIgnoreCase.Equals(p.Name, candidate))) return candidate;
            }
        }
        void Refresh()
        {
            count.Text = $"{profiles.Count} 个预设 · 编辑后可预览布局";
            list.Children.Clear();
            if (profiles.Count == 0)
            {
                var empty = Paragraph("还没有预设。点击“新建预设”，保存适合你的屏幕布局。", 14, new(0, 18, 0, 22));
                Identify(empty, "SceneManager.Empty", "场景库为空"); list.Children.Add(empty);
            }
            foreach (var profile in profiles)
            {
                var p = profile;
                var content = new StackPanel();
                var frame = new Border { Child = content, BorderThickness = new(0, 0, 0, 1), Padding = new(0, 12, 0, 12) };
                frame.SetResourceReference(Border.BorderBrushProperty, "Line");
                Identify(frame, "SceneManager.Row:" + p.Id, p.Name);
                var name = Paragraph(p.Name, 16); name.FontWeight = FontWeights.SemiBold;
                Identify(name, "SceneManager.Name:" + p.Id, p.Name); content.Children.Add(name);
                var detail = Paragraph($"{p.Displays.Count(d => d.Enabled)} 屏启用 · {p.Displays.Count(d => d.Enabled && d.Rotation % 180 == 90)} 屏竖向",
                    12, new(0, 4, 0, 10));
                detail.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); content.Children.Add(detail);
                var actions = new WrapPanel();
                var edit = ActionButton("编辑布局", "SceneManager.Edit:" + p.Id, "编辑布局：" + p.Name);
                edit.IsEnabled = editPreset != null;
                var rename = ActionButton("改名", "SceneManager.Rename:" + p.Id, "改名：" + p.Name);
                var copy = ActionButton("复制", "SceneManager.Copy:" + p.Id, "复制：" + p.Name);
                var delete = ActionButton("删除", "SceneManager.Delete:" + p.Id, "删除：" + p.Name);
                actions.Children.Add(edit); actions.Children.Add(rename); actions.Children.Add(copy); actions.Children.Add(delete);
                content.Children.Add(actions);

                var renamePanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new(0, 8, 0, 0) };
                var input = new TextBox { Text = p.Name, MaxLength = 60, MinHeight = 36 };
                Identify(input, "SceneManager.RenameInput:" + p.Id, "新的预设名称"); renamePanel.Children.Add(input);
                var renameActions = new WrapPanel { Margin = new(0, 8, 0, 0) };
                var renameSave = ActionButton("保存名称", "SceneManager.RenameSave:" + p.Id);
                var renameCancel = ActionButton("取消", "SceneManager.RenameCancel:" + p.Id);
                renameActions.Children.Add(renameSave); renameActions.Children.Add(renameCancel); renamePanel.Children.Add(renameActions);
                content.Children.Add(renamePanel);

                var deletePanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new(0, 8, 0, 0) };
                var prompt = Paragraph($"删除“{p.Name}”？不会改变当前屏幕布局。", 13);
                Identify(prompt, "SceneManager.DeletePrompt:" + p.Id, "确认删除预设"); deletePanel.Children.Add(prompt);
                var deleteActions = new WrapPanel { Margin = new(0, 8, 0, 0) };
                var deleteCancel = ActionButton("取消", "SceneManager.CancelDelete:" + p.Id);
                var deleteConfirm = ActionButton("确认删除", "SceneManager.ConfirmDelete:" + p.Id);
                deleteActions.Children.Add(deleteCancel); deleteActions.Children.Add(deleteConfirm); deletePanel.Children.Add(deleteActions);
                content.Children.Add(deletePanel);

                bool Current() => list.Children.Contains(frame) && !working && !leaving;
                edit.Click += (_, _) => { if (Current()) Leave(editPreset == null ? null : () => editPreset(CopyProfile(p))); };
                copy.Click += (_, _) =>
                {
                    if (Current()) Run("复制", () => Commit(profiles.Append(CopyProfile(p) with
                        { Id = Guid.NewGuid().ToString("N"), Name = CopyName(p.Name) }).ToList(), "已复制预设。"));
                };
                rename.Click += (_, _) =>
                {
                    if (!Current()) return;
                    deletePanel.Visibility = Visibility.Collapsed; renamePanel.Visibility = Visibility.Visible;
                    input.Text = p.Name; input.Focus(); input.SelectAll(); input.BringIntoView();
                };
                renameCancel.Click += (_, _) => { if (Current()) { renamePanel.Visibility = Visibility.Collapsed; rename.Focus(); } };
                renameSave.Click += (_, _) =>
                {
                    if (!Current() || renamePanel.Visibility != Visibility.Visible) return;
                    Run("改名", () =>
                    {
                        string value = input.Text.Trim();
                        if (value.Length is 0 or > 60) throw new ArgumentException("名称须为1至60个字符。");
                        if (value == p.Name) { renamePanel.Visibility = Visibility.Collapsed; return; }
                        Commit(profiles.Select(item => item.Id == p.Id ? item with { Name = value } : item).ToList(), "预设名称已保存。");
                    });
                };
                delete.Click += (_, _) =>
                {
                    if (!Current()) return;
                    renamePanel.Visibility = Visibility.Collapsed; deletePanel.Visibility = Visibility.Visible;
                    deleteCancel.Focus(); deletePanel.BringIntoView();
                };
                deleteCancel.Click += (_, _) => { if (Current()) { deletePanel.Visibility = Visibility.Collapsed; delete.Focus(); } };
                deleteConfirm.Click += (_, _) =>
                {
                    if (Current() && deletePanel.Visibility == Visibility.Visible)
                        Run("删除", () => Commit(profiles.Where(item => item.Id != p.Id).ToList(), "预设已删除，当前屏幕布局未改变。"));
                };
                list.Children.Add(frame);
            }
        }
        create.Click += (_, _) => Leave(createPreset);
        import.Click += (_, _) =>
        {
            if (importJson == null) return;
            Run("导入", () =>
            {
                string? json = importJson(); if (json == null) return;
                var incoming = ProfileStore.Parse(json);
                if (incoming.Count == 0) { Report("文件中没有可导入的预设。"); return; }
                Commit(profiles.Concat(incoming.Select(p => p with { Id = Guid.NewGuid().ToString("N") })).ToList(),
                    $"已导入 {incoming.Count} 个预设，原有预设已保留。");
            });
        };
        export.Click += (_, _) =>
        {
            if (exportJson == null) return;
            Run("导出", () =>
            {
                string json = JsonSerializer.Serialize(profiles, ProfileStore.Json);
                if (exportJson(json)) Report($"已导出 {profiles.Count} 个预设。");
            });
        };
        Refresh(); return root;

        static Button ActionButton(string text, string id, string? accessibleName = null)
        {
            var button = new Button { Content = text, Padding = new(12, 8, 12, 8), MinHeight = 36, Margin = new(0, 0, 8, 6) };
            Identify(button, id, accessibleName ?? text); return button;
        }
    }
    public static void Settings(Window owner, bool hotkeysEnabled, Action<bool> hotkeys, Action recovery,
        ConfirmationOptions? confirmation = null, Action<ConfirmationOptions>? saveConfirmation = null, Action? clearTrust = null,
        Action? restoreConfiguration = null, Action? exportDiagnostics = null, Action? editHotkeys = null, Action? checkUpdates = null)
    {
        var existing = owner.OwnedWindows.Cast<Window>().FirstOrDefault(window => Equals(window.Tag, "PingDa.Settings"));
        if (existing != null) { existing.Show(); existing.Activate(); return; }
        var w = Shell(owner, "设置与帮助", 600, 740);
        w.Tag = "PingDa.Settings";
        w.ResizeMode = ResizeMode.CanResize; w.MinWidth = 360; w.MinHeight = 320;
        bool startupEnabled;
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) startupEnabled = key?.GetValue("PingXu") != null;
        w.Content = CreateSettingsContent(hotkeysEnabled, hotkeys, recovery, startupEnabled, enabled =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enabled) key.SetValue("PingXu", "\"" + Environment.ProcessPath + "\""); else key.DeleteValue("PingXu", false);
        }, () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", Program.DataDirectory)
            { UseShellExecute = true }), confirmation, saveConfirmation, clearTrust,
            restoreConfiguration == null ? null : () => { w.Close(); restoreConfiguration(); },
            exportDiagnostics == null ? null : () => { w.Close(); exportDiagnostics(); },
            editHotkeys == null ? null : () => { w.Close(); editHotkeys(); },
            checkUpdates == null ? null : () => { w.Close(); checkUpdates(); });
        // Settings are a utility panel, not a decision that should lock the workspace.
        // Closing the foreground app also dismisses this panel, without writing unsaved choices.
        DependencyPropertyChangedEventHandler visibilityChanged = (_, _) => { if (!owner.IsVisible) w.Close(); };
        owner.IsVisibleChanged += visibilityChanged;
        w.Closed += (_, _) => owner.IsVisibleChanged -= visibilityChanged;
        w.Show();
    }

    // All external effects are injected, so the actual settings content can be tested offscreen.
    public static FrameworkElement CreateSettingsContent(bool hotkeysEnabled, Action<bool> hotkeys, Action recovery,
        bool startupEnabled, Action<bool> saveStartup, Action openData, ConfirmationOptions? confirmation = null,
        Action<ConfirmationOptions>? saveConfirmation = null, Action? clearTrust = null, Action? restoreConfiguration = null, Action? exportDiagnostics = null, Action? editHotkeys = null, Action? checkUpdates = null)
    {
        var options = confirmation ?? new ConfirmationOptions(); options.Validate();
        var body = new StackPanel { Margin = new(24) };
        body.Children.Add(Paragraph("让屏幕配合你的工作", 23));
        var updateButton = new Button { Content = "检查更新", MinHeight = 38, Margin = new(0,12,0,0), IsEnabled = checkUpdates != null };
        Identify(updateButton, "CheckUpdates", "检查软件更新");
        updateButton.Click += (_, _) => checkUpdates?.Invoke(); body.Children.Add(updateButton);
        var status = Paragraph("", margin: new(0, 12, 0, 0)); Identify(status, "SettingsStatus", "设置结果");
        var startup = new CheckBox { Content = Paragraph("登录 Windows 后启动屏搭"), IsChecked = startupEnabled, Margin = new(0, 22, 0, 10) };
        startup.Click += (_, _) =>
        {
            try { saveStartup(startup.IsChecked == true); startupEnabled = startup.IsChecked == true; }
            catch (Exception ex) { startup.IsChecked = startupEnabled; status.Text = "启动项设置失败：" + ex.Message; }
        };
        body.Children.Add(startup);
        var hk = new CheckBox { Content = Paragraph("启用全局快捷键"), IsChecked = hotkeysEnabled };
        hk.Click += (_, _) =>
        {
            try { hotkeys(hk.IsChecked == true); hotkeysEnabled = hk.IsChecked == true; }
            catch (Exception ex) { hk.IsChecked = hotkeysEnabled; status.Text = "快捷键保存失败：" + ex.Message; }
        };
        body.Children.Add(hk);
        var editKeys = new Button { Content = "修改快捷键", MinHeight = 38, Margin = new(0, 10, 0, 0), IsEnabled = editHotkeys != null };
        Identify(editKeys, "EditHotkeys", "修改预设快捷键");
        editKeys.Click += (_, _) => editHotkeys?.Invoke();
        body.Children.Add(editKeys);
        body.Children.Add(Paragraph("切换确认", 18, new(0, 24, 0, 10)));
        string group = "ConfirmationMode:" + Guid.NewGuid().ToString("N");
        RadioButton Choice(string id, string text, ConfirmationMode mode)
        {
            var radio = new RadioButton { Content = text, GroupName = group, Tag = mode,
                IsChecked = options.Mode == mode, Height = 44, Margin = new(0, 0, 0, 8) };
            radio.SetResourceReference(FrameworkElement.StyleProperty, "Direction");
            Identify(radio, id, text); body.Children.Add(radio); return radio;
        }
        var smart = Choice("ConfirmSmart", "智能确认（推荐）", ConfirmationMode.Smart);
        var always = Choice("ConfirmAlways", "每次确认", ConfirmationMode.Always);
        var never = Choice("ConfirmNever", "不再确认", ConfirmationMode.Never);
        body.Children.Add(Paragraph("智能：新布局需确认，可记住预设以跳过下次确认。每次：每次切换都需确认。", 12));
        var risk = Paragraph("关闭倒计时确认后，即使画面异常，也不会因等待超时而自动恢复。程序发现切换失败时仍会尝试恢复原布局。", 13, new(0, 10, 0, 10));
        risk.SetResourceReference(TextBlock.ForegroundProperty, "Acid"); Identify(risk, "NeverConfirmationRisk", "不再确认的风险提示");
        body.Children.Add(risk);
        body.Children.Add(Paragraph("倒计时", 14, new(0, 12, 0, 8)));
        var timeout = new ComboBox { MinHeight = 38 };
        Identify(timeout, "ConfirmationTimeout", "确认倒计时秒数");
        foreach (int seconds in new[] { 20, 30, 60 })
        {
            var item = new ComboBoxItem { Content = $"{seconds} 秒", Tag = seconds }; timeout.Items.Add(item);
            if (seconds == options.TimeoutSeconds) timeout.SelectedItem = item;
        }
        body.Children.Add(timeout);
        body.Children.Add(Paragraph("以上确认策略和倒计时仅在点击“保存确认设置”后生效。", 12, new(0, 10, 0, 0)));
        var save = new Button { Content = "保存确认设置", Margin = new(0, 12, 0, 0) };
        save.SetResourceReference(FrameworkElement.StyleProperty, "Primary"); Identify(save, "SaveConfirmation", "保存确认设置");
        body.Children.Add(save);
        var clear = new Button { Content = "重置确认记录", ToolTip = "不删除预设。智能确认模式下，使用预设时需重新确认。", Margin = new(0, 10, 0, 0), IsEnabled = clearTrust != null };
        Identify(clear, "ClearTrustedProfiles", "重置确认记录"); body.Children.Add(clear); body.Children.Add(status);
        bool saving = false;
        ConfirmationMode SelectedMode() => never.IsChecked == true ? ConfirmationMode.Never
            : always.IsChecked == true ? ConfirmationMode.Always : ConfirmationMode.Smart;
        void RefreshChoices()
        {
            risk.Visibility = SelectedMode() == ConfirmationMode.Never ? Visibility.Visible : Visibility.Collapsed;
            smart.IsEnabled = always.IsEnabled = never.IsEnabled = save.IsEnabled = saveConfirmation != null && !saving;
            timeout.IsEnabled = saveConfirmation != null && !saving && SelectedMode() != ConfirmationMode.Never;
            clear.IsEnabled = clearTrust != null && !saving;
        }
        foreach (var radio in new[] { smart, always, never }) radio.Checked += (_, _) => { RefreshChoices(); status.Text = "确认设置尚未保存。"; };
        timeout.SelectionChanged += (_, _) => status.Text = "确认设置尚未保存。";
        RefreshChoices();
        save.Click += (_, _) =>
        {
            if (saveConfirmation == null || saving) return;
            saving = true; RefreshChoices();
            try
            {
                if (timeout.SelectedItem is not ComboBoxItem { Tag: int seconds }) throw new InvalidOperationException("请选择倒计时时间。");
                var updated = new ConfirmationOptions(SelectedMode(), seconds); updated.Validate(); saveConfirmation(updated);
                status.Text = "确认设置已保存。";
            }
            catch (Exception ex) { status.Text = "确认设置保存失败：" + ex.Message; }
            finally { saving = false; RefreshChoices(); }
        };
        clear.Click += (_, _) =>
        {
            if (clearTrust == null || saving) return;
            saving = true; RefreshChoices();
            try { clearTrust(); status.Text = "确认记录已重置，预设仍保留。其他修改需点击保存。"; }
            catch (Exception ex) { status.Text = "确认记录重置失败：" + ex.Message; }
            finally { saving = false; RefreshChoices(); }
        };
        body.Children.Add(Paragraph("安全保护", 18, new(0, 24, 0, 8)));
        body.Children.Add(Paragraph("切换前保存原布局；需要确认时，超时由独立进程尝试恢复。物理断电、驱动故障或恢复进程被结束时，无法保证恢复成功。"));
        body.Children.Add(Paragraph("数据全部保存在本机，不需要账号，不上传屏幕信息。当前版本不调整 HDR、Windows 缩放、亮度、ICC 或音频设备。", 12, new(0, 14, 0, 0)));
        var open = new Button { Content = "打开数据与恢复日志", Margin = new(0, 20, 0, 0) };
        open.Click += (_, _) => { try { openData(); } catch (Exception ex) { status.Text = "打开日志失败：" + ex.Message; } }; body.Children.Add(open);
        var recoveryButton = new Button { Content = "检查恢复保护", Margin = new(0, 10, 0, 0) };
        recoveryButton.Click += (_, _) => recovery(); body.Children.Add(recoveryButton);
        var restoreButton = new Button { Content = "恢复配置备份", Margin = new(0, 10, 0, 0), IsEnabled = restoreConfiguration != null };
        Identify(restoreButton, "RestoreConfigurationBackups", "恢复配置备份");
        restoreButton.Click += (_, _) => restoreConfiguration?.Invoke(); body.Children.Add(restoreButton);
        var diagnosticButton = new Button { Content = "导出诊断报告", Margin = new(0, 10, 0, 0), IsEnabled = exportDiagnostics != null };
        Identify(diagnosticButton, "ExportDiagnostics", "导出诊断报告");
        diagnosticButton.Click += (_, _) => exportDiagnostics?.Invoke(); body.Children.Add(diagnosticButton);
        body.Children.Add(Paragraph($"屏搭 {BrandAssets.Version} · 本地预览版", 12, new(0, 16, 0, 0)));
        return ScrollBody(body);
    }

    static TextBlock Paragraph(string text, double size = 14, Thickness margin = default) => new()
        { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = margin };

    static ScrollViewer ScrollBody(Panel body)
    {
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false, UseLayoutRounding = true };
        scroll.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "Bg");
        scroll.SetResourceReference(TextElement.ForegroundProperty, "Ink");
        scroll.SetValue(TextElement.FontFamilyProperty, new System.Windows.Media.FontFamily("Microsoft YaHei UI"));
        scroll.SetValue(TextElement.FontSizeProperty, 14d);
        return scroll;
    }

    static void Identify(DependencyObject element, string id, string name)
    { AutomationProperties.SetAutomationId(element, id); AutomationProperties.SetName(element, name); }
}
