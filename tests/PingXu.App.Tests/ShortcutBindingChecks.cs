using System.IO;
using System.Windows.Input;
using PingXu.Core;
namespace PingXu.App.Tests;

internal static class ShortcutBindingChecks
{
    internal static void Register(Action<string, Action> test)
    {
        var profiles = new List<DisplayProfile> { new("a", "工作", []), new("b", "阅读", []) };
        test("托盘提示只出现一次，重新读取配置及修改别名后仍不重复", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "PingXu.TrayHint." + Guid.NewGuid().ToString("N"));
            try
            {
                if (!Preferences.ClaimTrayHint(directory)) throw new Exception("首次未提示");
                if (Preferences.ClaimTrayHint(directory)) throw new Exception("重复提示");
                Preferences.SaveInterface(true, new() { ["monitor"] = "阅读屏" }, directory);
                if (Preferences.ClaimTrayHint(directory) || !Preferences.Load(directory).Hotkeys) throw new Exception("修改设置丢失提示记录或覆盖其他字段");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
        void Check(bool value) { if (!value) throw new Exception("快捷键行为不符合预期"); }
        test("快捷键：旧配置保留默认值，清空后不再自动生成", () =>
        {
            Check(ShortcutBindings.Resolve(null, profiles)["a"] == new HotkeyGesture(3, 49));
            Check(ShortcutBindings.Resolve(new Dictionary<string, HotkeyGesture>(), profiles).Count == 0);
        });
        test("快捷键：绑定跟随预设身份而非顺序或名称", () =>
        {
            var saved = new Dictionary<string, HotkeyGesture> { ["a"] = new(6, 65) };
            var resolved = ShortcutBindings.Resolve(saved, [profiles[1], profiles[0] with { Name = "重命名" }]);
            Check(resolved.Count == 1 && resolved["a"] == new HotkeyGesture(6, 65));
            Check(ShortcutBindings.Resolve(saved, [profiles[1]]).Count == 0);
        });
        test("快捷键：录入正确转换组合及数字键", () =>
        {
            Check(HotkeyGesture.FromInput(Key.D2, ModifierKeys.Control | ModifierKeys.Alt) == new HotkeyGesture(3, 50));
            Check(HotkeyGesture.FromInput(Key.F5, ModifierKeys.Control | ModifierKeys.Shift).Label == "Ctrl + Shift + F5");
            Check(HotkeyGesture.FromInput(Key.NumPad2, ModifierKeys.Alt).Label == "Alt + Num 2");
        });
        test("快捷键：注册冲突撤销本次全部已注册项，不留下半套绑定", () =>
        {
            var saved = ShortcutBindings.Resolve(null, profiles);
            var released = new List<int>(); int next = 0;
            try { HotkeyRegistration.Register(saved, () => ++next, (id, _) => id == 1, released.Add); }
            catch (InvalidOperationException e) { Check(released.SequenceEqual([1]) && e.Message.Contains("Ctrl + Alt + 2")); return; }
            throw new Exception("快捷键冲突未报告");
        });
        test("快捷键：配置存取保留其他偏好和明确清空", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "PingXu-hotkeys-" + Guid.NewGuid().ToString("N"));
            try
            {
                new Preferences(true, new() { ["panel"] = "阅读" }) { Shortcuts = new() { ["a"] = new(6, 65) },
                    Confirmation = new(ConfirmationMode.Never, 60) }.Save(root);
                Preferences.SaveInterface(false, new() { ["panel"] = "办公" }, root);
                var read = Preferences.Load(root);
                Check(read.Shortcuts!["a"] == new HotkeyGesture(6, 65) && read.Confirmation == new ConfirmationOptions(ConfirmationMode.Never, 60));
                (read with { Shortcuts = new() }).Save(root);
                Check(Preferences.Load(root).Shortcuts!.Count == 0);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
        test("快捷键：重复组合不能作为有效偏好加载", () =>
        {
            const string json = """{"Hotkeys":true,"Aliases":{},"Shortcuts":{"a":{"Modifiers":3,"Key":49},"b":{"Modifiers":3,"Key":49}}}""";
            try { Preferences.Parse(json); }
            catch (InvalidDataException) { return; }
            throw new Exception("重复快捷键被接受");
        });
        test("快捷键：无修饰键不能作为全局快捷键保存", () =>
        {
            const string json = """{"Hotkeys":true,"Aliases":{},"Shortcuts":{"a":{"Modifiers":0,"Key":65}}}""";
            try { Preferences.Parse(json); }
            catch (InvalidDataException) { return; }
            throw new Exception("普通输入键被接受为全局快捷键");
        });
        test("快捷键：危险或不支持的组合不录入", () =>
        {
            foreach (var pair in new[] { (Key.F4, ModifierKeys.Alt | ModifierKeys.Shift), (Key.F12, ModifierKeys.Control),
                (Key.A, ModifierKeys.Windows), (Key.Delete, ModifierKeys.Control | ModifierKeys.Alt), (Key.A, ModifierKeys.Shift) })
            {
                try { HotkeyGesture.FromInput(pair.Item1, pair.Item2); }
                catch (InvalidDataException) { continue; }
                throw new Exception("接受了不支持的组合");
            }
        });
    }
}
