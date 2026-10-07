using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace PingXu.App.Tests;

internal static class DiagnosticExportChecks
{
    public static void Register(Action<string, Action> test)
    {
        test("诊断文件保存完整UTF8，拒绝覆盖已有文件且不残留临时文件", () =>
        {
            var directory = System.IO.Directory.CreateTempSubdirectory("PingXu-diagnostic-test-");
            try
            {
                var path = System.IO.Path.Combine(directory.FullName, "report.json");
                DiagnosticExport.SaveNewReport(path, "{\"测试\":true}");
                if (System.IO.File.ReadAllText(path) != "{\"测试\":true}") throw new Exception("content damaged");
                bool rejected = false;
                try { DiagnosticExport.SaveNewReport(path, "overwritten"); } catch (System.IO.IOException) { rejected = true; }
                if (!rejected || System.IO.File.ReadAllText(path) != "{\"测试\":true}" || directory.GetFiles().Length != 1) throw new Exception("overwrite or temp leak");
            }
            finally { directory.Delete(true); }
        });
        test("诊断报告先预览，保存取消不报告成功", () =>
        {
            int calls = 0;
            var view = DiagnosticExport.CreateContent("{\"safe\":true}", report => { calls++; if (report != "{\"safe\":true}") throw new Exception("report changed"); return false; });
            var preview = Find(view).OfType<TextBox>().Single();
            if (!preview.IsReadOnly || calls != 0 || preview.Text != "{\"safe\":true}") throw new Exception("preview not read-only or wrote on opening");
            Find(view).OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (calls != 1 || Find(view).OfType<TextBlock>().Any(t => t.Text == "诊断报告已保存。")) throw new Exception("cancel reported success");
        });
        test("诊断保存失败持续显示，允许重试", () =>
        {
            int calls = 0;
            var view = DiagnosticExport.CreateContent("{}", _ => { if (++calls == 1) throw new System.IO.IOException("磁盘已满"); return true; });
            var button = Find(view).OfType<Button>().Single();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!button.IsEnabled || !Find(view).OfType<TextBlock>().Any(t => t.Text.Contains("磁盘已满"))) throw new Exception("error hidden or retry blocked");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (calls != 2 || !Find(view).OfType<TextBlock>().Any(t => t.Text == "诊断报告已保存。")) throw new Exception("retry not successful");
        });
    }
    static IEnumerable<DependencyObject> Find(DependencyObject item)
    {
        yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>())
            foreach (var nested in Find(child)) yield return nested;
    }
}
