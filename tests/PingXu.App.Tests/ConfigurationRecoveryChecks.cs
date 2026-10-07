using System.IO;
using System.Windows;
using System.Windows.Controls;
using PingXu.Core;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;

namespace PingXu.App.Tests;

internal static class ConfigurationRecoveryChecks
{
    public static void Register(Action<string, Action> test)
    {
        test("恢复页面只在明确点击时恢复，取消确认保持页面", () =>
        {
            int restored = 0, closed = 0;
            var body = ConfigurationRecovery.CreateContent(_ => [new("test.json", DateTime.UtcNow)],
                (_, _) => { restored++; return false; }, () => closed++);
            var restore = Descendants(body).OfType<Button>().Single(b => b.Name == "RestoreConfiguration");
            if (restored != 0 || !restore.IsEnabled) throw new Exception("opening restored data or disabled valid selection");
            restore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (restored != 1 || closed != 0) throw new Exception("cancelled confirmation closed page");
        });
        test("恢复页面空列表禁用恢复，不制造默认备份", () =>
        {
            var body = ConfigurationRecovery.CreateContent(_ => [], (_, _) => throw new Exception("unexpected restore"), () => { });
            if (Descendants(body).OfType<Button>().Single(b => b.Name == "RestoreConfiguration").IsEnabled) throw new Exception("empty list can restore");
        });
        test("恢复期间占用切换锁，退出恢复流程后释放", () =>
        {
            bool busy = false, ran = false, failedAsExpected = false;
            try
            {
                ConfigurationRecovery.WithSwitchingBlocked(() => busy, value => busy = value, () =>
                {
                    ran = true;
                    if (!busy) throw new Exception("switching not blocked");
                    throw new InvalidDataException("simulated restore failure");
                });
            }
            catch (InvalidDataException) { failedAsExpected = true; }
            if (!ran || !failedAsExpected || busy) throw new Exception("restore action or lock cleanup not verified");
            busy = true; bool entered = false;
            ConfigurationRecovery.WithSwitchingBlocked(() => busy, value => busy = value, () => entered = true);
            if (entered || !busy) throw new Exception("existing transaction modified");
        });
        test("恢复偏好明确显示退出，恢复成功后关闭页面", () =>
        {
            int closed = 0; string? restoredName = null;
            var body = ConfigurationRecovery.CreateContent(_ => [new("test.json", DateTime.UtcNow)],
                (name, _) => { restoredName = name; return true; }, () => closed++);
            Descendants(body).OfType<ComboBox>().Single(c => c.Name == "ConfigurationKind").SelectedIndex = 1;
            var button = Descendants(body).OfType<Button>().Single(b => b.Name == "RestoreConfiguration");
            if (!button.Content.ToString()!.Contains("退出")) throw new Exception("restart consequence hidden");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (restoredName != "preferences.json" || closed != 1) throw new Exception("wrong restore target");
        });
        test("偏好兼容旧格式、自动备份且拒绝未来版本覆盖", () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "PingXu-preferences-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var old = Preferences.Parse("{\"Hotkeys\":false,\"Aliases\":{}}"); old.Save(root);
                (old with { Hotkeys = true }).Save(root);
                if (ConfigurationBackups.List(root, "preferences.json", s => Preferences.Parse(s)).Count != 1) throw new Exception("missing backup");
                var path = Path.Combine(root, "preferences.json"); var future = "{\"SchemaVersion\":999,\"Hotkeys\":false,\"Aliases\":{}}";
                File.WriteAllText(path, future);
                try { old.Save(root); throw new Exception("accepted future overwrite"); } catch (InvalidDataException) { }
                if (File.ReadAllText(path) != future) throw new Exception("overwritten");
            }
            finally { Directory.Delete(root, true); }
        });
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject item)
    {
        yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
