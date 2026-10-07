using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PingXu.Core;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = System.Windows.MessageBox;

namespace PingXu.App;

public static class ConfigurationRecovery
{
    public static void WithSwitchingBlocked(Func<bool> isBusy, Action<bool> setBusy, Action action)
    {
        if (isBusy()) return;
        setBusy(true);
        try { action(); } finally { setBusy(false); }
    }

    public static string? Show(Window owner, string directory, Action checkBeforeWrite)
    {
        string? restored = null;
        var window = new Window { Owner = owner, Title = "恢复配置备份", Width = 550, Height = 440,
            MinWidth = 400, MinHeight = 360, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        Action<string> Validator(string name) => name == "profiles.json" ? value => ProfileStore.Parse(value) : value => Preferences.Parse(value);
        window.Content = CreateContent(name => ConfigurationBackups.List(directory, name, Validator(name)), (name, backup) =>
        {
            var what = name == "profiles.json" ? "预设列表" : "偏好设置（屏幕名称、快捷键与切换确认方式）";
            var after = name == "profiles.json" ? "不会改变当前屏幕布局。" : "恢复后屏搭将退出，请重新打开。不会改变当前屏幕布局。";
            if (MessageBox.Show(window, $"将用 {backup.SavedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} 的备份替换{what}。\n\n当前配置会另存为原件，可在数据目录中找回。{after}",
                "确认恢复", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return false;
            checkBeforeWrite();
            ConfigurationBackups.Restore(directory, name, backup.Path, Validator(name));
            restored = name; return true;
        }, window.Close);
        window.ShowDialog(); return restored;
    }

    // No external effects until the injected restore callback confirms and performs the action.
    public static FrameworkElement CreateContent(Func<string, IReadOnlyList<ConfigurationBackup>> list,
        Func<string, ConfigurationBackup, bool> restore, Action close)
    {
        var body = new StackPanel { Margin = new(24) };
        TextBlock Text(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 14) };
        body.Children.Add(Text("找回之前的设置", 24));
        body.Children.Add(Text("保存修改时，自动保留最近 5 份不同的旧配置。选择备份后才会恢复，不会自动回退。"));
        var kind = new ComboBox { Name = "ConfigurationKind", MinHeight = 38, Margin = new(0, 0, 0, 14) };
        kind.Items.Add("预设"); kind.Items.Add("偏好设置");
        AutomationProperties.SetName(kind, "要恢复的配置"); body.Children.Add(kind);
        var backups = new ComboBox { Name = "ConfigurationBackup", MinHeight = 38, Margin = new(0, 0, 0, 14) };
        AutomationProperties.SetName(backups, "选择备份时间"); body.Children.Add(backups);
        var status = Text(""); body.Children.Add(status);
        var button = new Button { Name = "RestoreConfiguration", Content = "恢复预设", MinHeight = 42 };
        button.SetResourceReference(FrameworkElement.StyleProperty, "Primary"); body.Children.Add(button);
        string Name() => kind.SelectedIndex == 1 ? "preferences.json" : "profiles.json";
        void Refresh()
        {
            button.IsEnabled = false; backups.Items.Clear();
            button.Content = kind.SelectedIndex == 1 ? "恢复并退出屏搭" : "恢复预设";
            try
            {
                foreach (var backup in list(Name())) backups.Items.Add(new ComboBoxItem { Content = backup.SavedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), Tag = backup });
                backups.SelectedIndex = backups.Items.Count > 0 ? 0 : -1;
                status.Text = backups.Items.Count == 0 ? "还没有可用备份。下次修改并保存此配置时，会自动备份旧内容。" : "恢复前会另存当前文件；未点击恢复，不会修改任何配置。";
            }
            catch (Exception ex) { status.Text = "备份读取失败：" + ex.Message; }
        }
        backups.SelectionChanged += (_, _) => button.IsEnabled = backups.SelectedItem is ComboBoxItem { Tag: ConfigurationBackup };
        kind.SelectionChanged += (_, _) => Refresh();
        button.Click += (_, _) =>
        {
            if (backups.SelectedItem is not ComboBoxItem { Tag: ConfigurationBackup backup }) return;
            button.IsEnabled = false;
            try { if (restore(Name(), backup)) close(); }
            catch (Exception ex) { status.Text = "未能恢复配置：" + ex.Message; }
            finally { button.IsEnabled = true; }
        };
        kind.SelectedIndex = 0;
        return new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
}
