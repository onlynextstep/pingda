using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.IO;
using System.Text;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
namespace PingXu.App;
public static class DiagnosticExport
{
    public static void Show(Window owner, string report)
    {
        var window = new Window { Owner = owner, Title = "导出诊断报告", Width = 680, Height = 620,
            MinWidth = 420, MinHeight = 360, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.Content = CreateContent(report, content =>
        {
            var picker = new SaveFileDialog { Title = "保存诊断报告", Filter = "JSON 报告 (*.json)|*.json",
                FileName = $"PingXu-diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}.json", DefaultExt = ".json", AddExtension = true, OverwritePrompt = false };
            if (picker.ShowDialog(window) != true) return false;
            // A new report must not replace a configuration or any existing user file.
            SaveNewReport(picker.FileName, content);
            return true;
        });
        window.ShowDialog();
    }

    public static void SaveNewReport(string path, string report)
    {
        var target = Path.GetFullPath(path);
        if (File.Exists(target)) throw new IOException("该文件已存在，请换一个文件名；原文件未修改。");
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Encoding.UTF8.GetBytes(report); stream.Write(bytes); stream.Flush(true);
            }
            File.Move(temporary, target, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static FrameworkElement CreateContent(string report, Func<string, bool> save)
    {
        var body = new Grid { Margin = new(24) };
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var intro = new TextBlock { Text = "诊断报告预览\n\n根据应用上次读取的状态生成；需要最新信息时，请先刷新显示器。仅包含版本、屏幕布局参数和恢复状态，不含名称、设备标识或原始日志。ReportIndex 只是报告内序号，不是屏幕编号。保存后不会自动上传。",
            TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new(0, 0, 0, 16) };
        body.Children.Add(intro);
        var preview = new TextBox { Text = report, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new("Consolas"), FontSize = 13 };
        AutomationProperties.SetName(preview, "诊断报告内容，可复制"); Grid.SetRow(preview, 1); body.Children.Add(preview);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 12) };
        Grid.SetRow(status, 2); body.Children.Add(status);
        var button = new Button { Content = "保存到本地", MinHeight = 42 };
        button.SetResourceReference(FrameworkElement.StyleProperty, "Primary"); Grid.SetRow(button, 3); body.Children.Add(button);
        button.Click += (_, _) =>
        {
            button.IsEnabled = false;
            try { status.Text = save(report) ? "诊断报告已保存。" : "已取消保存。"; }
            catch (Exception ex) { status.Text = "未能保存：" + ex.Message; }
            finally { button.IsEnabled = true; }
        };
        return body;
    }
}
