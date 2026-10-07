using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PingXu.Core;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ContextMenu = System.Windows.Controls.ContextMenu;
using DataObject = System.Windows.DataObject;

namespace PingXu.App;

public static class HotkeyEditor
{
    public static void Show(Window owner, IReadOnlyList<DisplayProfile> profiles,
        Dictionary<string, HotkeyGesture> bindings, bool enabled, Action<bool, Dictionary<string, HotkeyGesture>> save)
    {
        var window = new Window { Owner = owner, Title = "编辑全局快捷键", Width = 640, Height = 580,
            MinWidth = 420, MinHeight = 360, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background, Foreground = Ink };
        window.Content = CreateContent(profiles, bindings, enabled, (active, updated) =>
        {
            save(active, updated);
            window.Close();
        }, window.Close);
        window.ShowDialog();
    }

    static readonly Brush Background = new SolidColorBrush(Color.FromRgb(21, 22, 22));
    static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(240, 241, 235));
    static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(220, 255, 66));

    public static FrameworkElement CreateContent(IReadOnlyList<DisplayProfile> profiles,
        Dictionary<string, HotkeyGesture> bindings, bool enabled, Action<bool, Dictionary<string, HotkeyGesture>> save,
        Action? cancel = null)
    {
        var draft = new Dictionary<string, HotkeyGesture>(bindings, bindings.Comparer);
        var root = new Grid { Margin = new Thickness(24), Background = Background };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel();
        header.Children.Add(Text("全局快捷键", 24));
        header.Children.Add(Text("点击录入框后按组合键。支持 Ctrl 或 Alt + A–Z、0–9、F1–F11、数字小键盘 0–9，可加 Shift。Esc 取消本次录入。", 13));
        var active = new CheckBox { Content = "启用全局快捷键", IsChecked = enabled, Foreground = Ink, Margin = new Thickness(0, 12, 0, 16) };
        Id(active, "HotkeyEnabled"); header.Children.Add(active); root.Children.Add(header);
        var rows = new StackPanel();
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var status = Text("", 13); status.Foreground = Accent;
        Id(status, "HotkeyStatus"); Grid.SetRow(status, 2); root.Children.Add(status);
        TextBox? recording = null;
        Action? stopRecording = null;
        bool saving = false;
        var inputErrors = new Dictionary<TextBox, string>();
        bool ValidateDraft()
        {
            if (inputErrors.Count > 0) { status.Text = inputErrors.Values.First(); return false; }
            try { ShortcutBindings.Validate(draft); status.Text = ""; return true; }
            catch (Exception ex) { status.Text = ex.Message; return false; }
        }
        for (int index = 0; index < profiles.Count; index++)
        {
            var profile = profiles[index];
            var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var name = Text(profile.Name, 14); name.VerticalAlignment = VerticalAlignment.Center;
            name.Margin = new Thickness(0, 0, 12, 0); row.Children.Add(name);
            var box = new TextBox { IsReadOnly = true, IsReadOnlyCaretVisible = false, AllowDrop = false,
                ContextMenu = new ContextMenu(), MinHeight = 40, VerticalContentAlignment = VerticalAlignment.Center,
                Background = Background, Foreground = Ink, BorderBrush = Accent, Padding = new Thickness(8),
                ToolTip = "按组合键录入；Esc 保留原快捷键" };
            Id(box, "HotkeyCapture" + index); AutomationProperties.SetName(box, profile.Name + " 快捷键");
            InputMethod.SetIsInputMethodEnabled(box, false);
            string Label() => draft.TryGetValue(profile.Id, out var gesture) ? gesture.Label : "点击录入";
            void Stop() { box.Text = Label(); if (recording == box) { recording = null; stopRecording = null; } }
            void Begin()
            {
                if (recording == box) return;
                stopRecording?.Invoke(); recording = box; stopRecording = Stop;
                box.Text = "请按组合键…";
            }
            box.Text = Label();
            box.GotKeyboardFocus += (_, _) => Begin();
            box.PreviewMouseLeftButtonDown += (_, _) => Begin();
            box.LostKeyboardFocus += (_, _) => { Stop(); ValidateDraft(); };
            DataObject.AddPastingHandler(box, (_, e) => e.CancelCommand());
            box.PreviewKeyDown += (_, e) =>
            {
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.Tab) return;
                e.Handled = true;
                if (key == Key.Escape) { inputErrors.Remove(box); Stop(); ValidateDraft(); return; }
                Begin();
                if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
                try
                {
                    var gesture = HotkeyGesture.FromInput(key, e.KeyboardDevice.Modifiers);
                    gesture.Validate(); draft[profile.Id] = gesture; inputErrors.Remove(box); Stop(); ValidateDraft();
                }
                catch (Exception ex)
                {
                    inputErrors[box] = profile.Name + "：" + ex.Message + " 旧快捷键未改变；请重新录入、按 Esc 保留旧值，或清除。";
                    ValidateDraft();
                }
            };
            Grid.SetColumn(box, 1); row.Children.Add(box);
            var clear = MakeButton("清除", "HotkeyClear" + index);
            clear.Margin = new Thickness(8, 0, 0, 0);
            AutomationProperties.SetName(clear, "清除 " + profile.Name + " 的快捷键");
            clear.Click += (_, _) => { draft.Remove(profile.Id); inputErrors.Remove(box); Stop(); ValidateDraft(); };
            Grid.SetColumn(clear, 2); row.Children.Add(clear); rows.Children.Add(row);
        }
        if (profiles.Count == 0) rows.Children.Add(Text("暂无预设。创建预设后可为它设置快捷键。", 14));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var saveButton = MakeButton("保存", "HotkeySave"); saveButton.Background = Accent; saveButton.Foreground = Background;
        var cancelButton = MakeButton("取消", "HotkeyCancel"); cancelButton.Margin = new Thickness(12, 0, 0, 0);
        saveButton.Click += (_, _) =>
        {
            if (saving) return;
            saving = true; saveButton.IsEnabled = false;
            try
            {
                stopRecording?.Invoke();
                if (!ValidateDraft()) return;
                save(active.IsChecked == true, new Dictionary<string, HotkeyGesture>(draft, draft.Comparer));
                status.Text = "快捷键已保存。";
            }
            catch (Exception ex) { status.Text = "未能保存：" + ex.Message; }
            finally { saving = false; saveButton.IsEnabled = true; }
        };
        cancelButton.Click += (_, _) => cancel?.Invoke();
        buttons.Children.Add(saveButton); buttons.Children.Add(cancelButton);
        Grid.SetRow(buttons, 3); root.Children.Add(buttons); ValidateDraft();
        return root;
    }

    static TextBlock Text(string text, double size) => new() { Text = text, FontSize = size,
        Foreground = Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
    static Button MakeButton(string text, string id)
    {
        var button = new Button { Content = text, MinHeight = 40, Padding = new Thickness(16, 8, 16, 8),
            Background = Background, Foreground = Ink, BorderBrush = Accent };
        Id(button, id); return button;
    }
    static void Id(DependencyObject element, string id) => AutomationProperties.SetAutomationId(element, id);
}
