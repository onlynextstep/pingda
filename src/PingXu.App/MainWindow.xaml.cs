using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using PingXu.Core;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using RadioButton = System.Windows.Controls.RadioButton;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using MessageBox = System.Windows.MessageBox;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace PingXu.App;
public partial class MainWindow : Window
{
    readonly IDisplayService service;
    readonly AppUpdates updates;
    readonly ProfileStore store = new(Program.DataDirectory);
    DesktopSnapshot? snapshot;
    List<DisplayProfile> profiles = [];
    DisplayProfile? draft;
    string? selected;
    bool populating, busy, exiting, dirty;
    bool hotkeysEnabled;
    bool profilesReady, recoveryBlocked;
    int libraryRevision;
    readonly Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase);
    System.Windows.Forms.NotifyIcon? tray;
    System.Drawing.Icon? trayIcon;
    System.Windows.Interop.HwndSource? source;
    readonly Dictionary<int, string> hotkeyTargets = [];
    Dictionary<string, HotkeyGesture>? shortcutBindings;
    bool editingHotkeys;
    int nextHotkeyId = 6000;
    static readonly SolidColorBrush Acid = new(Color.FromRgb(220, 255, 66));
    static readonly SolidColorBrush Muted = new(Color.FromRgb(167, 172, 165));
    readonly DispatcherTimer hardwareTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    readonly StatusFeedback statusFeedback;
    DateTime lastSignal = DateTime.MinValue;
    bool DisplayCapacitySupported => snapshot != null && DisplayLimits.CheckConnected(snapshot.Displays).Success;

    public MainWindow(IDisplayService service, Func<DisplayProfile, Task>? switchLayout = null)
    {
        this.service = service; this.switchLayout = switchLayout; InitializeComponent(); Style = (Style)FindResource(typeof(Window)); AttachWorkspace();
        updates = new AppUpdates(this, () => !busy && !dirty && !RecoveryMarker.GuardianRunning(), path =>
        {
            Process.Start(new ProcessStartInfo(path, "/UPDATE") { UseShellExecute = true });
            exiting = true;
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            trayIcon?.Dispose();
            System.Windows.Application.Current.Shutdown();
        });
        Loaded += async (_, _) => { await Task.Delay(5000); if (!exiting) await updates.CheckAsync(true); };
        statusFeedback = new(message =>
        {
            StatusLabel.Text = message ?? "";
            StatusNotice.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
        }, (delay, tick) => new StatusNoticeTimer(Dispatcher, delay, tick));
        Closed += (_, _) => statusFeedback.Dispose();
        Loaded += async (_, _) => { try { var preferences = Preferences.Load(); hotkeysEnabled = preferences.Hotkeys; shortcutBindings = preferences.Shortcuts; foreach (var pair in preferences.Aliases) aliases[pair.Key] = pair.Value; } catch (Exception e) { Status("偏好读取失败：" + e.Message); Program.Log(e); } try { recoveryBlocked = RecoveryMarker.NeedsAttention(); } catch { recoveryBlocked = true; } InitializeTray(); await RefreshHardware(); SetHotkeys(hotkeysEnabled); if (recoveryBlocked) Status("上次切换结果尚未确认，已暂停新切换。请在设置中检查恢复保护。"); };
        SourceInitialized += (_, _) => { source = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle); source?.AddHook(Hook); };
        hardwareTimer.Tick += async (_, _) => await RefreshPendingHardware(); hardwareTimer.Start();
        Closing += (_, e) =>
        {
            if (exiting) return;
            e.Cancel = true; Hide();
            if (tray == null) return;
            try
            {
                if (Preferences.ClaimTrayHint())
                    tray.ShowBalloonTip(2000, "屏搭", "已收起到托盘，右键图标可退出。", System.Windows.Forms.ToolTipIcon.Info);
            }
            catch (Exception error) { Program.Log(error); } // A bad preferences file must not block hiding or be overwritten.
        };
    }
    IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        // Let WPF continue processing its size constraints after updating maximized bounds.
        if (msg == 0x0024) WindowBounds.Apply(hwnd, l);
        if (msg == 0x007e || msg == 0x0219) lastSignal = DateTime.UtcNow;
        if (msg == 0x0312 && hotkeyTargets.TryGetValue(w.ToInt32(), out var profileId))
        {
            handled = true;
            var profile = profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile != null && !busy && !editingHotkeys) _ = SwitchScene(profile);
        }
        return IntPtr.Zero;
    }
    void InitializeTray()
    {
        trayIcon = BrandAssets.CreateTrayIcon();
        tray = new() { Icon = trayIcon, Visible = true, Text = "屏搭 · 多屏工作空间" }; tray.DoubleClick += (_, _) => OpenWindow(); RebuildTray();
    }
    internal void OpenWindow() { if (exiting) return; Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
    void RebuildTray()
    {
        if (tray == null) return; var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Renderer = new System.Windows.Forms.ToolStripProfessionalRenderer(new TrayAppearance()) { RoundedEdges = false };
        menu.BackColor = System.Drawing.Color.FromArgb(21,22,22);
        menu.ForeColor = System.Drawing.Color.FromArgb(239,241,234);
        menu.ShowImageMargin = false;
        menu.Padding = new System.Windows.Forms.Padding(5);
        menu.Items.Add("打开屏搭", null, (_, _) => OpenWindow()); menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        foreach (var p in profiles) { var item = menu.Items.Add(p.Name); item.Click += async (_, _) => await SwitchScene(p); }
        menu.Items.Add("恢复上次布局", null, (_, _) => Restore_Click(this, new RoutedEventArgs()));
        // The guardian restores an unconfirmed layout if its owner exits.
        // Exiting the UI must not depend on a display operation becoming responsive.
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator()); menu.Items.Add("退出", null, (_, _) => { exiting = true; tray.Visible = false; tray.Dispose(); trayIcon?.Dispose(); System.Windows.Application.Current.Shutdown(); });
        foreach (System.Windows.Forms.ToolStripItem item in menu.Items) { item.ForeColor = menu.ForeColor; item.Padding = new System.Windows.Forms.Padding(12,6,12,6); }
        var old = tray.ContextMenuStrip; tray.ContextMenuStrip = menu; old?.Dispose();
    }
    async Task RefreshPendingHardware(bool immediately = false)
    {
        if (busy || lastSignal == DateTime.MinValue || (!immediately && DateTime.UtcNow - lastSignal <= TimeSpan.FromSeconds(1))) return;
        if (dirty || editingPresetId != null)
        { Status("显示器连接已变化；编辑已保留，保存或取消编辑后自动刷新。"); return; }
        await RefreshActualWorkspace();
    }

    // Refresh live state without loading/saving the preset library or preferences.
    // All scene decisions use this capture; a failed capture must never authorize a switch.
    async Task<bool> RefreshActualWorkspace()
    {
        if (busy) return false;
        busy = true;
        var signal = lastSignal;
        try
        {
            studioDrag?.Cancel(); UpdateWorkspaceActions(); PopulateInspector(); RenderScenes();
            snapshot = await Task.Run(service.Capture);
            // A notification received while Capture was in flight still needs another refresh.
            if (lastSignal == signal) lastSignal = DateTime.MinValue;
            ShowActualWorkspace();
            ConnectionLabel.Text = $"{snapshot.Displays.Count(d => d.Connected)} 台在线";
            TopologyLabel.Text = $"{snapshot.Displays.Count(d => d.Enabled)} 块屏幕";
            var capacity = DisplayLimits.CheckConnected(snapshot.Displays);
            if (!capacity.Success) { Status(capacity.Message); return false; }
            return true;
        }
        catch (Exception e) { Status("读取当前显示布局失败，未切换：" + e.Message); return false; }
        finally { busy = false; PopulateInspector(); UpdateWorkspaceActions(); RenderScenes(); }
    }

    async Task RefreshHardware()
    {
        if (busy) return; busy = true; ApplyButton.IsEnabled = false;
        try
        {
            snapshot = await Task.Run(service.Capture);
            var displays = snapshot.Displays;
            // Discovery is read-only: generated labels must not become permanent personal aliases.
            if (!File.Exists(System.IO.Path.Combine(Program.DataDirectory, "baseline.json")) && displays.Any(d => d.Enabled)) ProfileStore.AtomicWrite(System.IO.Path.Combine(Program.DataDirectory, "baseline.json"), JsonSerializer.Serialize(snapshot, ProfileStore.Json));
            profilesReady = false; libraryRevision++;
            try { profiles = store.Load(); profilesReady = true; } catch (Exception e) { Status("预设读取失败，已锁定保存以保护原文件。请刷新重试：" + e.Message); profiles = []; }
            if (DisplayCapacitySupported && profiles.Count == 0 && !File.Exists(System.IO.Path.Combine(Program.DataDirectory, "profiles.json"))) { GenerateProfiles(displays); if (profiles.Count > 0) store.Save(profiles); }
            draft = new("current", "当前布局", displays.Select(ToTarget).ToList()); editingPresetId = null; creatingPreset = false; dirty = false; selected = displays.FirstOrDefault(d => d.Enabled)?.Id;
            ConnectionLabel.Text = $"{displays.Count(d => d.Connected)} 台在线";
            TopologyLabel.Text=$"{displays.Count(d=>d.Enabled)} 块屏幕";
            var currentScene = profiles.FirstOrDefault(p => SameLayout(p.Displays, draft.Displays));
            SceneTitle.Text = currentScene?.Name ?? "当前布局"; PreviewTag.Text = "当前布局";
            Subtitle.Text = "拖动屏幕调整位置。";
            RenderScenes(); RenderCanvas(); PopulateInspector(); RebuildTray();
            RestoreButton.IsEnabled = File.Exists(System.IO.Path.Combine(Program.DataDirectory, "previous.json"));
            var capacity = DisplayLimits.CheckConnected(displays);
            if (!capacity.Success) Status(capacity.Message);
        }
        catch (Exception e) { snapshot = null; draft = null; ConnectionLabel.Text = "无法读取显示器"; Status(e.Message); LayoutCanvas.Children.Clear(); Program.Log(e); }
        finally { busy = false; PopulateInspector(); UpdateWorkspaceActions(); RenderScenes(); if (source != null) SetHotkeys(hotkeysEnabled); }
    }
    static DisplayTarget ToTarget(DisplayInfo d) => new(d.Id, d.Enabled, d.Primary, d.X, d.Y, d.Width, d.Height, d.Rotation, d.RefreshRate);
    void GenerateProfiles(List<DisplayInfo> displays)
    {
        profiles = DefaultProfileFactory.Create(displays);
        var numbers = DisplayPresentation.Numbers(displays);
        var labels = DisplayPresentation.Labels(displays, aliases, numbers);
        // A generated single-screen candidate names its actual target, not a machine-specific role.
        for (int i = 1; i < profiles.Count; i++)
        {
            var target = profiles[i].Displays.Single(d => d.Enabled);
            var name = $"仅 {numbers[target.Id]:00} · {labels[target.Id]}";
            profiles[i] = profiles[i] with { Name = name.Length > 60 ? name[..59] + "…" : name };
        }
    }
    void SelectProfile(DisplayProfile p)
    {
        if (busy) return; studioDrag?.Cancel(); draft = p with { Displays = p.Displays.ToList() };
        selected = draft.Displays.FirstOrDefault(d => d.Enabled)?.Id; RenderScenes(); RenderCanvas(); PopulateInspector();
    }
    Dictionary<string, int> DisplayNumbers() => DisplayPresentation.Numbers(snapshot?.Displays ?? [], draft?.Displays);
    Dictionary<string, string> DisplayLabels() => DisplayPresentation.Labels(snapshot?.Displays ?? [], aliases, DisplayNumbers());
    string Alias(string id) => DisplayLabels().GetValueOrDefault(id, "未连接显示器");
    int ScreenNumber(string id) => DisplayNumbers().GetValueOrDefault(id, 0);
    void RenderScenes()
    {
        if (SceneStrip == null) return;
        SceneStrip.Children.Clear();
        double available = Math.Max(0, ScenesScroll?.ActualWidth ?? 0);
        int columns = Math.Max(1, Math.Min(5, profiles.Count));
        double dpiScale = VisualTreeHelper.GetDpi(SceneStrip).DpiScaleX;
        double spacing = Math.Round(6 * dpiScale) / dpiScale;
        // Round down in device pixels: rounding each fractional card up can overflow the viewport.
        double width = Math.Max(196, Math.Floor((available - (columns - 1) * spacing) / columns * dpiScale) / dpiScale);
        bool overflow = (width + spacing) * profiles.Count - spacing > available + 1;
        double cardHeight = Math.Min(130, Math.Max(108, (ScenesScroll?.ActualHeight ?? 178) - (overflow ? 18 : 0) - 24));
        for (int i = 0; i < profiles.Count; i++)
        {
            var p = profiles[i];
            bool selectedScene = editingPresetId == p.Id;
            bool current = snapshot != null && SameLayout(p.Displays, snapshot.Displays.Select(ToTarget));
            bool highlighted = pendingSceneId != null ? pendingSceneId == p.Id : current;
            bool modified = selectedScene && draft != null && !SameLayout(p.Displays, draft.Displays);
            int missing = p.Displays.Count(d => d.Enabled && snapshot?.Displays.Any(live => live.Connected && StringComparer.OrdinalIgnoreCase.Equals(live.Id,d.Id)) != true);
            var ink = highlighted ? Brushes.Black : (System.Windows.Media.Brush)FindResource("Ink");
            var secondary = highlighted ? new SolidColorBrush(Color.FromRgb(50, 59, 16)) : Muted;
            var content = new Grid { Height = cardHeight };
            content.RowDefinitions.Add(new() { Height = new(32) });
            content.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new() { Height = new(24) });
            var title = new DockPanel();
            title.Children.Add(new TextBlock { Text = $"{i + 1:00}", FontFamily = new("Bahnschrift"),
                FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = ink, Margin = new(0, 0, 13, 0) });
            title.Children.Add(new TextBlock { Text = p.Name, FontSize = 16, FontWeight = FontWeights.Medium,
                Foreground = ink, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            content.Children.Add(title);
            var enabled = p.Displays.Where(d => d.Enabled).OrderBy(d => d.X).ToList();
            double miniWidth = Math.Min(226, width - 30);
            var mini = new Canvas { Width = miniWidth, Height = 64, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(mini, 1); content.Children.Add(mini);
            if (enabled.Count > 0)
            {
                double minX = enabled.Min(d => d.X), minY = enabled.Min(d => d.Y);
                double spanX = enabled.Max(d => (double)d.X + d.Width) - minX;
                double spanY = enabled.Max(d => (double)d.Y + d.Height) - minY;
                double scale = Math.Min((miniWidth - 10) / Math.Max(1, spanX), 48 / Math.Max(1, spanY));
                foreach (var d in enabled)
                {
                    double x = (miniWidth - spanX * scale) / 2 + (d.X - minX) * scale;
                    double y = (54 - spanY * scale) / 2 + (d.Y - minY) * scale;
                    double w = Math.Max(2, d.Width * scale - 3), h = Math.Max(2, d.Height * scale - 3);
                    var rect = new Rectangle { Width = w, Height = h, Stroke = ink, StrokeThickness = 1.3, Fill = Brushes.Transparent };
                    Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y); mini.Children.Add(rect);
                    if (d.Primary)
                    {
                        mini.Children.Add(new Line { X1 = x + w / 2, X2 = x + w / 2, Y1 = y + h, Y2 = y + h + 4, Stroke = ink, StrokeThickness = 1.2 });
                        mini.Children.Add(new Line { X1 = x + w / 2 - 7, X2 = x + w / 2 + 7, Y1 = y + h + 4, Y2 = y + h + 4, Stroke = ink, StrokeThickness = 1.2 });
                    }
                }
            }
            var bottom = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Bottom };
            string state = missing > 0 ? $"缺 {missing} 屏" : current ? (modified ? "当前 · 编辑中" : "当前") : modified ? "已修改" : selectedScene ? "编辑中" : "";
            if (state.Length > 0)
            {
                var badge = new Border { Background = current ? Brushes.Black : new SolidColorBrush(Color.FromRgb(49, 55, 28)),
                    Padding = new(6, 2, 6, 2), CornerRadius = new(2), Margin = new(8, 0, 0, 0),
                    Child = new TextBlock { Text = state, FontSize = 10, Foreground = Acid } };
                DockPanel.SetDock(badge, Dock.Right); bottom.Children.Add(badge);
            }
            bottom.Children.Add(new TextBlock { Text = $"{enabled.Count} 屏",
                FontSize = 11, Foreground = secondary, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetRow(bottom, 2); content.Children.Add(bottom);
            var button = new Button { Content = content, Width = width, Margin = new(0, 0, i == profiles.Count - 1 ? 0 : spacing, 0),
                Padding = new(15, 10, 15, 10), HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = highlighted ? Acid : (System.Windows.Media.Brush)FindResource("ShelfSurface"),
                BorderBrush = highlighted || selectedScene ? Acid : (System.Windows.Media.Brush)FindResource("Line"),
                IsEnabled = !busy && pendingSceneId == null && DisplayCapacitySupported,
                ToolTip = p.Name + (selectingPreset || editingPresetId != null ? " · 点击编辑，不切换屏幕" : " · 点击切换；修改请先点“编辑预设”") +
                    (hotkeyTargets.ContainsValue(p.Id) && shortcutBindings?.TryGetValue(p.Id, out var shortcut) == true ? $"\n{shortcut.Label}：按当前确认设置切换" : "") };
            System.Windows.Automation.AutomationProperties.SetAutomationId(button, "Scene:" + p.Id);
            if (highlighted && pendingSceneId != null)
            {
                button.ApplyTemplate();
                if (button.Template?.FindName("Chrome", button) is Border chrome) chrome.Opacity = 1;
            }
            System.Windows.Automation.AutomationProperties.SetName(button, p.Name + (state.Length > 0 ? "，" + state : "") + (selectingPreset || editingPresetId != null ? "，点击编辑，不切换屏幕" : "，点击直接切换"));
            button.Click += async (_, _) => await ActivateSceneCard(p); SceneStrip.Children.Add(button);
        }
    }
    static bool SameLayout(IEnumerable<DisplayTarget> left, IEnumerable<DisplayTarget> right) => DisplayPresentation.SameLayout(left, right);
    void Canvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderCanvas();
    void RenderCanvas()
    {
        if (LayoutCanvas == null) return;
        UpdateWorkspaceActions();
        if (draft == null || snapshot == null) { LayoutCanvas.Children.Clear(); return; }
        StudioDrawing.DrawLayout(LayoutCanvas, draft, snapshot.Displays, DisplayLabels(), DisplayNumbers(),
            selected, id => { selected = id; PopulateInspector(); RenderCanvas(); });
        var enabled = draft.Displays.Where(d => d.Enabled).OrderBy(d => d.X);
        SceneHint.Text = string.Join("  /  ", enabled.Select(d => Alias(d.Id)
            + (d.Primary ? " · 主屏" : "") + (d.Rotation % 180 == 90 ? " · 竖向" : "")));
        TopologyLabel.Text = $"{enabled.Count()} 块屏幕";
        ApplyButton.IsEnabled = dirty && editingPresetId == null && !busy && !recoveryBlocked && DisplayCapacitySupported && enabled.Any() && enabled.All(d => snapshot.Displays.Any(live => live.Connected && StringComparer.OrdinalIgnoreCase.Equals(live.Id,d.Id)));
        RenderScenes();
    }
    void PopulateInspector()
    {
        populating = true; try
        {
            var d = draft?.Displays.FirstOrDefault(d => d.Id == selected); Inspector.IsEnabled = d != null && !busy && DisplayCapacitySupported;
            if (renamingMonitorId != null && renamingMonitorId != selected) CancelMonitorName_Click(this, new RoutedEventArgs());
            if (d == null) return;
            var live = snapshot?.Displays.FirstOrDefault(t => t.Id == selected);
            MonitorName.Text = Alias(d.Id); MonitorInfo.Text = $"屏幕编号 {ScreenNumber(d.Id):00} · " + (aliases.ContainsKey(d.Id) ? "自定义名称" : "系统名称") + "\n" + (live?.Connected == true ? "已连接" : "未连接") + "  ·  " + (d.Enabled ? "已启用" : "已停用") + (dirty || editingPresetId != null ? "（编辑中）" : ""); EnabledToggle.IsChecked = d.Enabled; PrimaryToggle.IsChecked = d.Primary; PrimaryToggle.IsEnabled = d.Enabled;
            RotationBox.SelectedIndex = d.Rotation / 90;var directions=new[]{Rotation0,Rotation90,Rotation180,Rotation270};for(int i=0;i<directions.Length;i++)directions[i].IsChecked=i==d.Rotation/90; XBox.Text = d.X.ToString(); YBox.Text = d.Y.ToString(); ModeBox.Items.Clear();
            EnabledToggle.IsEnabled = live?.Connected == true;
            var modes = (live?.Modes ?? []).Where(m => m.Width > 0 && m.Height > 0 && m.RefreshRate > 0).ToList(); var baseMode = d.Rotation % 180 == 90 ? new DisplayMode(d.Height, d.Width, d.RefreshRate) : new DisplayMode(d.Width, d.Height, d.RefreshRate); if (baseMode.Width > 0 && baseMode.Height > 0 && baseMode.RefreshRate > 0 && !modes.Contains(baseMode)) modes.Insert(0, baseMode);
            foreach (var m in modes.Distinct().OrderByDescending(m => m.Width * m.Height).ThenByDescending(m => m.RefreshRate)) { var item = new ComboBoxItem { Content = $"{m.Width} × {m.Height} · {m.RefreshRate} Hz", Tag = m }; ModeBox.Items.Add(item); if (m == baseMode) ModeBox.SelectedItem = item; }
        }
        finally { populating = false; }
    }
    void UpdateTarget(Func<DisplayTarget, DisplayTarget> update, bool align = false)
    {
        if (populating || busy || draft == null || selected == null) return;
        var old = draft; draft = draft with { Displays = draft.Displays.Select(d => d.Id == selected ? update(d) : d).ToList() };
        if (draft.Displays.All(d => !d.Enabled)) { draft = old; Status("不能停用最后一块屏幕。"); PopulateInspector(); return; }
        if (align) AlignDraft(); MarkDraftChanged(); RenderCanvas(); PopulateInspector();
    }
    void AlignDraft()
    {
        if (draft == null) return; var list = draft.Displays.OrderBy(d => d.X).ToList(); var main = list.FirstOrDefault(d => d.Enabled && d.Primary) ?? list.LastOrDefault(d => d.Enabled); if (main == null) return;
        int x = 0, origin = 0; for (int i = 0; i < list.Count; i++) { if (!list[i].Enabled) { list[i] = list[i] with { Primary = false }; continue; } if (list[i].Id == main.Id) origin = x; list[i] = list[i] with { X = x, Y = 0, Primary = list[i].Id == main.Id }; x += list[i].Width; }
        draft = draft with { Displays = list.Select(d => d.Enabled ? d with { X = d.X - origin } : d).ToList() };
    }
    void Enabled_Click(object sender, RoutedEventArgs e)
    {
        if (populating || busy || draft == null || selected == null || !DisplayCapacitySupported) return;
        bool enable = EnabledToggle.IsChecked == true;
        try
        {
            var live = snapshot?.Displays.FirstOrDefault(d => d.Id == selected);
            if (enable && (live == null || !live.Connected)) throw new InvalidOperationException("未检测到该显示器，请确认电源、输入源和连接线后重新检测。");
            var mode = enable && live != null ? DisplayEnableMode.Select(live) : null;
            UpdateTarget(d => d with { Enabled = enable, Primary = d.Primary && enable,
                Width = mode == null ? d.Width : d.Rotation % 180 == 90 ? mode.Height : mode.Width,
                Height = mode == null ? d.Height : d.Rotation % 180 == 90 ? mode.Width : mode.Height,
                RefreshRate = mode?.RefreshRate ?? d.RefreshRate }, true);
        }
        catch (Exception ex) { Status(ex.Message); PopulateInspector(); }
    }
    void Primary_Click(object sender, RoutedEventArgs e) { if (populating || busy || draft == null || selected == null) return; var p = draft.Displays.Single(d => d.Id == selected); if (!p.Enabled) return; draft = draft with { Displays = draft.Displays.Select(d => d.Enabled ? d with { Primary = d.Id == selected, X = d.X - p.X, Y = d.Y - p.Y } : d).ToList() }; MarkDraftChanged(); PopulateInspector(); RenderCanvas(); }
    void Rotation_Changed(object sender, SelectionChangedEventArgs e) { if (populating || RotationBox.SelectedItem is not ComboBoxItem item) return; int angle = int.Parse(item.Tag.ToString()!); UpdateTarget(d => { bool swap = d.Rotation % 180 != angle % 180; return d with { Rotation = angle, Width = swap ? d.Height : d.Width, Height = swap ? d.Width : d.Height }; }, true); }
    void Direction_Checked(object sender,RoutedEventArgs e){if(populating)return;if(busy){PopulateInspector();return;}if(sender is RadioButton button)RotationBox.SelectedIndex=int.Parse(button.Tag.ToString()!);}
    void LayoutNav_Click(object sender,RoutedEventArgs e){InspectorScroll.ScrollToTop();LayoutCanvas.Focus();}
    void Minimize_Click(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    void Maximize_Click(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    void CloseWindow_Click(object sender,RoutedEventArgs e)=>Close();
    void Scenes_SizeChanged(object sender,SizeChangedEventArgs e)=>RenderScenes();
    void Mode_Changed(object sender, SelectionChangedEventArgs e) { if (populating || ModeBox.SelectedItem is not ComboBoxItem { Tag: DisplayMode m }) return; UpdateTarget(d => d with { Width = d.Rotation % 180 == 90 ? m.Height : m.Width, Height = d.Rotation % 180 == 90 ? m.Width : m.Height, RefreshRate = m.RefreshRate }, true); }
    void Position_Click(object sender, RoutedEventArgs e) { if (int.TryParse(XBox.Text, out int x) && int.TryParse(YBox.Text, out int y)) UpdateTarget(d => d with { X = x, Y = y }); else Status("位置须为整数像素。"); }
    void Align_Click(object sender, RoutedEventArgs e) { if (busy || !DisplayCapacitySupported) return; AlignDraft(); MarkDraftChanged(); RenderCanvas(); PopulateInspector(); }
    async void Refresh_Click(object sender, RoutedEventArgs e) { if (!CanLeaveEditor()) return; await RefreshHardware(); }
    void Status(string message)
    {
        ConfigureStatus("需要处理", false, false);
        statusFeedback.Show(message);
        if (message.Contains("恢复保护") || message.Contains("暂停新切换"))
        {
            StatusHeading.Text = "切换已暂停";
            statusDetails = () => Settings_Click(this, new RoutedEventArgs());
            StatusDetailsButton.Content = "检查恢复设置 ↗";
            StatusDetailsButton.Visibility = Visibility.Visible;
        }
    }
    Action? statusDetails;
    void ConfigureStatus(string title, bool progress, bool success)
    {
        StatusHeading.Text = title; StatusIcon.Text = success ? "↗" : progress ? "··" : "!";
        StatusHeading.Visibility = success ? Visibility.Collapsed : Visibility.Visible;
        StatusBody.Margin = success ? new Thickness(0) : new Thickness(0, 6, 0, 0);
        StatusLabel.FontSize = success ? 14 : 12;
        StatusLabel.Foreground = success ? (System.Windows.Media.Brush)FindResource("Ink") : Muted;
        StatusIcon.Foreground = progress || success ? Acid : new SolidColorBrush(Color.FromRgb(239, 193, 117));
        StatusAccent.Fill = StatusIcon.Foreground;
        StatusProgressLine.Visibility = progress ? Visibility.Visible : Visibility.Collapsed;
        StatusDetailsButton.Visibility = Visibility.Collapsed; StatusDetailsButton.Content = "查看详情 ↗"; statusDetails = null;
    }
    void StatusProgress(string title)
    {
        ConfigureStatus(title, true, false);
        statusFeedback.Show("屏幕可能短暂变黑，请稍候。");
    }
    void StatusSuccess(string message)
    {
        ConfigureStatus("", false, true);
        statusFeedback.ShowSuccess(message);
    }
    void StatusDetails_Click(object sender, RoutedEventArgs e) => statusDetails?.Invoke();

    sealed class StatusNoticeTimer : IDisposable
    {
        readonly DispatcherTimer timer;
        readonly EventHandler handler;
        public StatusNoticeTimer(Dispatcher dispatcher, TimeSpan delay, Action tick)
        {
            timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = delay };
            handler = (_, _) => { Dispose(); tick(); };
            timer.Tick += handler;
            timer.Start();
        }
        public void Dispose() { timer.Stop(); timer.Tick -= handler; }
    }
    async void Apply_Click(object sender, RoutedEventArgs e) { if (editingPresetId == null) await ApplyDraft(); }
    Task ApplyDraft() => draft == null ? Task.CompletedTask : ApplyProfile(draft);
    void ShowDisplayFailure(string details, DisplayFailureStage stage)
    {
        var desktop = details.Contains("Windows 错误 5）") ? InputDesktopObservation.Read() : null;
        var message = DisplayFailure.Create(details, stage, desktop);
        Status(message.Explanation + " " + message.Outcome);
        StatusHeading.Text = message.Title;
        statusDetails = () => Dialogs.ShowFailure(this, message.Title, message.Explanation, message.Outcome, message.NextStep, message.Details);
        StatusDetailsButton.Content = "查看详情 ↗";
        StatusDetailsButton.Visibility = Visibility.Visible;
        Dialogs.ShowFailure(this, message.Title, message.Explanation, message.Outcome, message.NextStep, message.Details);
    }
    async Task ApplyProfile(DisplayProfile candidate)
    {
        if (busy) return; if (recoveryBlocked) { Status("恢复状态未确认，请先在设置中检查恢复保护。"); return; }
        candidate = candidate with { Displays = candidate.Displays.ToList() }; studioDrag?.Cancel();
        busy = true; ApplyButton.IsEnabled = false; Inspector.IsEnabled = false; Window? confirm = null; string? path = null; bool mayHaveChanged = false;
        try
        {
            StatusProgress("正在准备切换…");
            var before = await Task.Run(service.Capture); LayoutPlanner.Check(candidate, before.Displays); var check = await Task.Run(() => service.Validate(candidate)); if (!check.Success) { ShowDisplayFailure(check.Message, DisplayFailureStage.BeforeChange); return; }
            var preferences = Preferences.Load();
            var decision = ConfirmationPolicy.Evaluate(preferences.Confirmation, candidate, profilesReady ? profiles : [], before.Displays, DisplayEnvironment.Capture(), preferences.TrustedProfiles);
            bool remember = false, dontAskAgain = false;
            using var self = Process.GetCurrentProcess(); var req = new TrialRequest(Guid.NewGuid().ToString("N"), self.Id, self.StartTime.ToUniversalTime().Ticks, candidate, before, decision.RequiresConfirmation, decision.TimeoutSeconds);
            path = System.IO.Path.Combine(Program.DataDirectory, "transactions", req.Id + ".json"); ProfileStore.AtomicWrite(path, JsonSerializer.Serialize(req, ProfileStore.Json));
            new RecoveryMarker(path, true).Save(); recoveryBlocked = true;
            var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true }; psi.ArgumentList.Add("--guardian"); psi.ArgumentList.Add(path); using var guardian = Process.Start(psi) ?? throw new InvalidOperationException("无法启动恢复保护进程，未改变显示器。"); mayHaveChanged = true;
            StatusProgress("正在切换屏幕…"); var sw = Stopwatch.StartNew(); TextBlock? countdown = null; bool terminal = false;
            while (sw.Elapsed.TotalMinutes < 2)
            {
                await Task.Delay(200); if (!File.Exists(path + ".status")) { if (guardian.HasExited) throw new InvalidOperationException("恢复进程意外退出，请检查布局。"); continue; }
                TrialProgress? progress; try { progress = JsonSerializer.Deserialize<TrialProgress>(File.ReadAllText(path + ".status")); } catch (IOException) { continue; }
                if (progress == null) continue;
                switch (progress.Phase)
                {
                    case "applying": StatusProgress("正在切换屏幕…"); break;
                    case "committing": StatusProgress("正在保存布局…"); break;
                    case "restoring": StatusProgress("正在恢复原布局…"); break;
                    case "trial":
                        ConfigureStatus("请确认显示是否正常", false, false);
                        statusFeedback.Show("请在确认窗口选择保留或恢复。"); break;
                    case "reverted" when progress.SafeToContinue: StatusSuccess("已恢复原布局。"); break;
                }
                if (progress.Phase == "trial")
                {
                    if (confirm == null) { OpenWindow(); confirm = Dialogs.ConfirmDisplay(this, (value, skip) => { remember = value; dontAskAgain = skip; ProfileStore.AtomicWrite(path + ".confirm", req.Id); }, () => ProfileStore.AtomicWrite(path + ".cancel", req.Id), out countdown, req.TimeoutSeconds, decision.CanRemember); confirm.Show(); }
                    if (countdown != null) countdown.Text = $"{progress.Seconds} 秒后自动恢复";
                }
                if (progress.Phase is "committed" or "reverted" or "error")
                {
                    terminal = true; confirm?.Close(); confirm = null;
                    if (progress.SafeToContinue) { new RecoveryMarker(path, false).Save(); recoveryBlocked = false; }
                    else Status(progress.Message + "；恢复未确认，已暂停新切换。请在设置中检查恢复保护。");
                    if (progress.Phase == "committed" && progress.SafeToContinue)
                    {
                        StatusSuccess("布局已应用。");
                        if (dontAskAgain)
                        {
                            try
                            {
                                var latest = Preferences.Load();
                                (latest with { Confirmation = latest.Confirmation with { Mode = ConfirmationMode.Never } }).Save();
                                StatusSuccess("布局已应用，已关闭切换确认。");
                            }
                            catch (Exception e) { Program.Log(e); Status("布局已应用，但确认设置未能保存。"); }
                        }
                        else if (remember && decision.CanRemember && decision.TrustKey is { } key)
                        {
                            try { Preferences.Remember(key); StatusSuccess("布局已应用，已记住此预设。"); }
                            catch (Exception e) { Program.Log(e); Status("布局已应用，但未能记住此预设，下次仍需确认。"); }
                        }
                    }
                    if (progress.Phase == "error")
                    {
                        if (!IsVisible) OpenWindow();
                        ShowDisplayFailure(progress.Message, progress.SafeToContinue ? DisplayFailureStage.SafeAfterError : DisplayFailureStage.UnknownAfterChange);
                    }
                    break;
                }
                if (guardian.HasExited) throw new InvalidOperationException("恢复进程意外退出，原布局是否恢复尚未确认。已暂停新切换，请在设置中检查恢复保护。");
            }
            if (!terminal) { if (path != null) ProfileStore.AtomicWrite(path + ".cancel", "timeout"); Status("切换超过等待时间，已请求恢复。请勿继续切换，请检查显示器与恢复日志。"); }
        }
        catch (Exception ex) { Program.Log(ex); ShowDisplayFailure(ex.Message, mayHaveChanged ? DisplayFailureStage.UnknownAfterChange : DisplayFailureStage.BeforeChange); }
        finally { confirm?.Close(); busy = false; Inspector.IsEnabled = true; await RefreshHardware(); }
    }
    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (busy || draft == null || snapshot == null) return; if (!profilesReady) { Status("预设库尚未成功读取，为保护原文件已禁止保存。请先刷新。"); return; }
        BeginPreset(null, true);
    }
    void CommitProfiles(List<DisplayProfile> updated,int expectedRevision)
    {
        // ShowDialog pumps Dispatcher: recheck at the actual write boundary, not only on entry.
        if(busy||!profilesReady||expectedRevision!=libraryRevision)throw new InvalidOperationException("预设库在操作期间发生变化或未能读取，未覆盖原文件。请关闭对话框、刷新后重试。");
        store.Save(updated);profiles=updated.ToList();libraryRevision++;
    }
    void Manage_Click(object sender, RoutedEventArgs e)
    {
        if (selectingPreset || editingPresetId != null)
        { if (CanLeaveEditor()) ShowActualWorkspace(); }
        else EnterPresetSelection();
    }
    void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        try
        {
            Preferences? preferences = null;
            try { preferences = Preferences.Load(); }
            catch (Exception ex) { Program.Log(ex); MessageBox.Show(this, "偏好设置无法读取，已禁止覆盖原文件。\n你仍可使用“恢复配置备份”。\n\n" + ex.Message, "偏好设置需要处理"); }
            Dialogs.Settings(this, hotkeysEnabled, enabled => SaveHotkeys(enabled, CurrentShortcuts()), AcknowledgeRecovery,
                preferences?.Confirmation,
                preferences == null ? null : options => { (Preferences.Load() with { Confirmation = options }).Save(); StatusSuccess("切换确认设置已保存。"); },
                preferences == null ? null : () => { (Preferences.Load() with { TrustedProfiles = [] }).Save(); StatusSuccess("确认记录已重置，预设仍保留。"); },
                RestoreConfiguration, () => DiagnosticExport.Show(this,
                    DiagnosticReport.Create(snapshot, typeof(BrandAssets).Assembly.GetName().Version!, Environment.OSVersion.Version,
                        profilesReady ? profiles.Count : null, recoveryBlocked)),
                preferences == null || !profilesReady ? null : EditHotkeys,
                () => _ = updates.CheckAsync(false));
        }
        catch (Exception ex) { Status(ex.Message); MessageBox.Show(ex.Message, "设置未保存"); }
    }
    void RestoreConfiguration()
    {
        if (busy || RecoveryMarker.GuardianRunning() || !CanLeaveEditor()) return;
        string? restored = null;
        ConfigurationRecovery.WithSwitchingBlocked(() => busy, value => busy = value, () =>
        {
            restored = ConfigurationRecovery.Show(this, Program.DataDirectory, () =>
            {
                if (RecoveryMarker.GuardianRunning()) throw new InvalidOperationException("屏幕切换或恢复仍在进行，未恢复配置。请结束切换后重试。");
            });
        });
        if (restored == "preferences.json")
        {
            // Explicitly announced and confirmed in the restore dialog. Never let stale in-memory settings overwrite the restored file.
            exiting = true; System.Windows.Application.Current.Shutdown();
        }
        else if (restored == "profiles.json")
        {
            profiles = store.Load(); profilesReady = true; libraryRevision++;
            ShowActualWorkspace(); LibraryChanged(); StatusSuccess("预设已恢复，当前屏幕布局未改变。");
        }
    }
    void AcknowledgeRecovery()
    {
        if (RecoveryMarker.GuardianRunning()) { MessageBox.Show("屏幕切换或恢复还在进行，请稍后再操作。如果长时间没有变化，请打开 Windows 显示设置检查屏幕。", "恢复保护"); return; }
        if (!recoveryBlocked) { MessageBox.Show("当前没有待处理的切换问题，可以正常使用。", "恢复保护"); return; }
        if (MessageBox.Show("请先在 Windows 显示设置中检查方向、主屏和已启用的屏幕，并确认画面正常。\n\n确认后会允许再次切换，不会改变当前布局。切换前的布局记录会继续保留。\n\n当前画面已经正常，可以继续切换了吗？", "检查恢复保护", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { var marker = RecoveryMarker.Read(); new RecoveryMarker(marker?.TransactionPath ?? "人工确认", false).Save(); recoveryBlocked = false; UpdateWorkspaceActions(); Status("已按你的确认解除锁定，请刷新布局后再操作。"); } catch (Exception ex) { Status(ex.Message); }
    }
    void RenameMonitor_Click(object sender, RoutedEventArgs e)
    {
        if (busy || selected == null) return;
        renamingMonitorId = selected; MonitorRenameBox.Text = Alias(selected);
        MonitorName.Visibility = RenameMonitorButton.Visibility = Visibility.Collapsed;
        MonitorRenamePanel.Visibility = Visibility.Visible; MonitorRenameBox.Focus(); MonitorRenameBox.SelectAll();
    }
    string? renamingMonitorId;
    void CancelMonitorName_Click(object sender, RoutedEventArgs e)
    {
        renamingMonitorId = null; MonitorRenamePanel.Visibility = Visibility.Collapsed;
        MonitorName.Visibility = RenameMonitorButton.Visibility = Visibility.Visible;
    }
    void SaveMonitorName_Click(object sender, RoutedEventArgs e)
    {
        if (busy || renamingMonitorId == null) return;
        var name = MonitorRenameBox.Text.Trim();
        if (name.Length is < 1 or > 40) { MonitorRenameBox.ToolTip = "请输入1–40个字符"; MonitorRenameBox.Focus(); return; }
        try {
            var updated = new Dictionary<string,string>(aliases, StringComparer.OrdinalIgnoreCase) { [renamingMonitorId] = name };
            Preferences.SaveInterface(hotkeysEnabled, updated); aliases[renamingMonitorId] = name;
            CancelMonitorName_Click(sender,e); PopulateInspector(); RenderCanvas();
        } catch (Exception ex) { Status("名称未保存：" + ex.Message); }
    }
    void MonitorRename_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; SaveMonitorName_Click(sender,e); }
        else if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; CancelMonitorName_Click(sender,e); }
    }
    void ResetMonitorName_Click(object sender, RoutedEventArgs e)
    {
        if (busy || selected == null || !aliases.ContainsKey(selected)) return;
        try
        {
            var updated = new Dictionary<string, string>(aliases, StringComparer.OrdinalIgnoreCase);
            updated.Remove(selected); Preferences.SaveInterface(hotkeysEnabled, updated); aliases.Remove(selected);
            PopulateInspector(); RenderCanvas(); StatusSuccess("已恢复系统提供的名称；不会改动设备或预设。");
        }
        catch (Exception ex) { Status("名称未保存：" + ex.Message); }
    }
    void SetHotkeys(bool enabled)
    {
        UnregisterHotkeys();
        if (!enabled || editingHotkeys || !profilesReady || source == null) return;
        try { foreach (var pair in RegisterHotkeys(CurrentShortcuts())) hotkeyTargets.Add(pair.Key, pair.Value); }
        catch (Exception ex) { Status("快捷键未启用：" + ex.Message); }
    }
    Dictionary<string, HotkeyGesture> CurrentShortcuts()
    {
        shortcutBindings ??= ShortcutBindings.Resolve(null, profiles);
        return ShortcutBindings.Resolve(shortcutBindings, profiles);
    }
    void UnregisterHotkeys()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        foreach (int id in hotkeyTargets.Keys) DesktopTools.UnregisterHotKey(handle, id);
        hotkeyTargets.Clear();
    }
    Dictionary<int, string> RegisterHotkeys(Dictionary<string, HotkeyGesture> bindings)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) throw new InvalidOperationException("窗口尚未就绪，请稍后重试。");
        return HotkeyRegistration.Register(bindings, () => { if (nextHotkeyId >= 0xBFFF) nextHotkeyId = 6000; return nextHotkeyId++; },
            (id, key) => DesktopTools.RegisterHotKey(handle, id, key.Modifiers | 0x4000, key.Key),
            id => DesktopTools.UnregisterHotKey(handle, id));
    }
    void SaveHotkeys(bool enabled, Dictionary<string, HotkeyGesture> bindings)
    {
        ShortcutBindings.Validate(bindings);
        UnregisterHotkeys();
        Dictionary<int, string> probe = [];
        bool saved = false;
        try
        {
            if (enabled) probe = RegisterHotkeys(bindings);
            (Preferences.Load() with { Hotkeys = enabled, Shortcuts = new(bindings) }).Save();
            hotkeysEnabled = enabled; shortcutBindings = new(bindings);
            foreach (var pair in probe) hotkeyTargets.Add(pair.Key, pair.Value);
            probe = []; saved = true;
        }
        finally
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            foreach (int id in probe.Keys) DesktopTools.UnregisterHotKey(handle, id);
            if (!saved) SetHotkeys(hotkeysEnabled);
            RenderScenes();
        }
    }
    void EditHotkeys()
    {
        if (busy || !profilesReady || RecoveryMarker.GuardianRunning()) return;
        editingHotkeys = true; UnregisterHotkeys();
        try
        {
            ConfigurationRecovery.WithSwitchingBlocked(() => busy, value => busy = value,
                () => HotkeyEditor.Show(this, profiles, CurrentShortcuts(), hotkeysEnabled, SaveHotkeys));
        }
        finally { editingHotkeys = false; if (hotkeyTargets.Count == 0) SetHotkeys(hotkeysEnabled); RenderScenes(); }
    }
    void Rescue_Click(object sender, RoutedEventArgs e) { try { var count = DesktopTools.RescueWindows(); StatusSuccess($"已找回 {count} 个离屏窗口（不含受权限保护的应用）。"); } catch (Exception ex) { Status(ex.Message); } }
    void MoreTools_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }
    async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return; try { var path = System.IO.Path.Combine(Program.DataDirectory, "previous.json"); if (!File.Exists(path)) return; var s = JsonSerializer.Deserialize<DesktopSnapshot>(File.ReadAllText(path))!; await SwitchScene(new("restore", "上次布局", s.Displays.Select(ToTarget).ToList())); } catch (Exception ex) { Status(ex.Message); }
    }
    void Identify_Click(object sender, RoutedEventArgs e) { if (busy || snapshot == null) return; DesktopTools.Identify(snapshot.Displays.Where(d => d.Enabled).ToDictionary(d => d.DeviceName, d => (ScreenNumber(d.Id), Alias(d.Id)), StringComparer.OrdinalIgnoreCase)); }
}
