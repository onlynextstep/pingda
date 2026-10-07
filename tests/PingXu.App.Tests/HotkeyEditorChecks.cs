using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PingXu.Core;

namespace PingXu.App.Tests;

internal static class HotkeyEditorChecks
{
    public static void Register(Action<string, Action> test)
    {
        test("快捷键清除仅改草稿，取消不保存", () =>
        {
            var bindings = new Dictionary<string, HotkeyGesture> { ["a"] = new(2, 65) };
            int saves = 0, cancels = 0;
            var root = HotkeyEditor.CreateContent([new("a", "工作", [])], bindings, true,
                (_, _) => saves++, () => cancels++);
            Check(Box(root, "HotkeyCapture0").IsReadOnly);
            Click(root, "HotkeyClear0");
            Check(bindings.Count == 1 && saves == 0);
            Click(root, "HotkeyCancel");
            Check(cancels == 1 && saves == 0);
        });
        test("快捷键真实按键录入并只在保存时回调", () =>
        {
            var source = new Dictionary<string, HotkeyGesture>();
            Dictionary<string, HotkeyGesture>? result = null;
            bool? enabled = null;
            var root = HotkeyEditor.CreateContent([new("a", "工作", [])], source, true,
                (on, value) => { enabled = on; result = value; });
            var box = Box(root, "HotkeyCapture0");
            Press(box, Key.A, ModifierKeys.Control);
            Check(result == null && source.Count == 0 && box.Text.Contains("A"));
            Find<CheckBox>(root, "HotkeyEnabled").IsChecked = false;
            Click(root, "HotkeySave");
            Check(enabled == false && result!["a"].Modifiers == 2 && result["a"].Key == 65 && source.Count == 0);
        });
        test("快捷键Alt SystemKey及Shift录入", () =>
        {
            Dictionary<string, HotkeyGesture>? result = null;
            var root = HotkeyEditor.CreateContent([new("a", "工作", [])], [], true, (_, value) => result = value);
            Press(Box(root, "HotkeyCapture0"), Key.F2, ModifierKeys.Alt | ModifierKeys.Shift, system: true);
            Click(root, "HotkeySave");
            Check(result!["a"].Modifiers == 5 && result["a"].Key == 113);
        });
        test("快捷键单修饰键等待且Esc保留旧值", () =>
        {
            Dictionary<string, HotkeyGesture>? result = null;
            var root = HotkeyEditor.CreateContent([new("a", "工作", [])], new() { ["a"] = new(2, 65) }, true, (_, value) => result = value);
            var box = Box(root, "HotkeyCapture0");
            string old = box.Text;
            Press(box, Key.LeftCtrl, ModifierKeys.Control);
            Check(box.Text != old && result == null);
            Press(box, Key.Escape, ModifierKeys.None);
            Check(box.Text == old);
            Click(root, "HotkeySave");
            Check(result!["a"].Key == 65);
        });
        test("快捷键无效键显示错误且不替换旧值", () =>
        {
            Dictionary<string, HotkeyGesture>? result = null;
            var root = HotkeyEditor.CreateContent([new("a", "工作", [])], new() { ["a"] = new(2, 65) }, true, (_, value) => result = value);
            var box = Box(root, "HotkeyCapture0");
            Press(box, Key.F12, ModifierKeys.Control);
            Check(Find<TextBlock>(root, "HotkeyStatus").Text.Length > 0 && result == null);
            Press(box, Key.Escape, ModifierKeys.None);
            Click(root, "HotkeySave"); Check(result!["a"].Key == 65);
        });
        test("快捷键无效录入失焦后保留错误并阻止静默保存旧值", () =>
        {
            int calls = 0;
            var root = HotkeyEditor.CreateContent([new("a", "工作", [])], new() { ["a"] = new(2, 65) }, true, (_, _) => calls++);
            var box = Box(root, "HotkeyCapture0");
            Press(box, Key.F12, ModifierKeys.Control);
            string error = Find<TextBlock>(root, "HotkeyStatus").Text;
            box.RaiseEvent(new KeyboardFocusChangedEventArgs(new TestKeyboard(ModifierKeys.None), 0, box,
                Find<Button>(root, "HotkeySave")) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            Check(error.Length > 0 && Find<TextBlock>(root, "HotkeyStatus").Text == error);
            Click(root, "HotkeySave"); Check(calls == 0);
            Press(box, Key.Escape, ModifierKeys.None);
            Click(root, "HotkeySave"); Check(calls == 1);
        });
        foreach (bool clear in new[] { false, true })
            test("快捷键无效录入可以修正或清除后保存 clear=" + clear, () =>
            {
                Dictionary<string, HotkeyGesture>? result = null;
                var root = HotkeyEditor.CreateContent([new("a", "工作", [])], new() { ["a"] = new(2, 65) }, true, (_, value) => result = value);
                var box = Box(root, "HotkeyCapture0");
                Press(box, Key.F12, ModifierKeys.Control);
                Click(root, "HotkeySave"); Check(result == null);
                if (clear) Click(root, "HotkeyClear0"); else Press(box, Key.B, ModifierKeys.Control);
                Check(Find<TextBlock>(root, "HotkeyStatus").Text.Length == 0);
                Click(root, "HotkeySave");
                Check(result != null && (clear ? result.Count == 0 : result["a"].Key == 66));
            });
        test("快捷键冲突inline阻止保存且清除后可保存", () =>
        {
            int calls = 0;
            var root = HotkeyEditor.CreateContent([new("a", "工作", []), new("b", "娱乐", [])],
                new() { ["a"] = new(2, 65) }, true, (_, _) => calls++);
            Press(Box(root, "HotkeyCapture1"), Key.A, ModifierKeys.Control);
            Check(Find<TextBlock>(root, "HotkeyStatus").Text.Length > 0);
            Click(root, "HotkeySave"); Check(calls == 0);
            Click(root, "HotkeyClear1"); Click(root, "HotkeySave"); Check(calls == 1);
        });
        test("快捷键保存异常保留草稿且可重试，回调字典隔离", () =>
        {
            int calls = 0;
            var root = HotkeyEditor.CreateContent([new("a", "工作", [])], new() { ["a"] = new(2, 65) }, true,
                (_, value) => { calls++; Check(value.ContainsKey("a")); value.Clear(); if (calls == 1) throw new System.IO.IOException("保存失败测试"); });
            Click(root, "HotkeySave");
            Check(Find<TextBlock>(root, "HotkeyStatus").Text.Contains("保存失败测试") && Find<Button>(root, "HotkeySave").IsEnabled);
            Click(root, "HotkeySave"); Check(calls == 2);
        });
        test("快捷键预览文本输入与粘贴无法改变绑定", () =>
        {
            Dictionary<string, HotkeyGesture>? result = null;
            var root = HotkeyEditor.CreateContent([new("a", "工作", [])], new() { ["a"] = new(2, 65) }, true, (_, value) => result = value);
            var box = Box(root, "HotkeyCapture0");
            var paste = new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText, "原始文本"), false, DataFormats.UnicodeText);
            box.RaiseEvent(paste); Check(paste.CommandCancelled && box.IsReadOnly && !box.AllowDrop);
            Click(root, "HotkeySave"); Check(result!["a"].Key == 65);
        });
        test("快捷键录入禁用输入法避免中文IME吞键", () =>
        {
            var root = HotkeyEditor.CreateContent([new("a", "工作", [])], [], true, (_, _) => { });
            Check(!InputMethod.GetIsInputMethodEnabled(Box(root, "HotkeyCapture0")));
        });
        foreach (var (key, virtualKey) in new[] { (Key.D0, 0x30u), (Key.NumPad0, 0x60u), (Key.NumPad9, 0x69u), (Key.F1, 0x70u), (Key.F11, 0x7Au), (Key.Z, 0x5Au) })
            test("快捷键录入范围边界 " + key, () =>
            {
                Dictionary<string, HotkeyGesture>? result = null;
                var root = HotkeyEditor.CreateContent([new("a", "工作", [])], [], true, (_, value) => result = value);
                Press(Box(root, "HotkeyCapture0"), key, ModifierKeys.Control | ModifierKeys.Shift);
                Click(root, "HotkeySave"); Check(result!["a"].Key == virtualKey && result["a"].Modifiers == 6);
            });
        test("快捷键空预设离屏布局及保存", () =>
        {
            int calls = 0;
            var root = HotkeyEditor.CreateContent([], [], false, (on, value) => { Check(!on && value.Count == 0); calls++; });
            root.Measure(new Size(560, 480)); root.Arrange(new Rect(0, 0, 560, 480)); root.UpdateLayout();
            Check(PresentationSource.FromVisual(root) == null);
            Click(root, "HotkeySave"); Check(calls == 1);
        });
    }

    static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Walk(child)) yield return nested;
    }
    static T Find<T>(DependencyObject root, string id) where T : DependencyObject =>
        Walk(root).OfType<T>().Single(x => AutomationProperties.GetAutomationId(x) == id);
    static TextBox Box(DependencyObject root, string id) => Find<TextBox>(root, id);
    static void Click(DependencyObject root, string id) => Find<Button>(root, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static void Check(bool condition) { if (!condition) throw new Exception("快捷键编辑器行为不符合预期"); }
    static void Press(TextBox box, Key key, ModifierKeys modifiers, bool system = false)
    {
        var args = new KeyEventArgs(new TestKeyboard(modifiers), new OffscreenSource(), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        if (system) typeof(KeyEventArgs).GetMethod("MarkSystem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(args, null);
        box.RaiseEvent(args); Check(args.Handled);
    }
    // Supplies modifier state to real routed key events without touching the physical keyboard.
    sealed class TestKeyboard(ModifierKeys modifiers) : KeyboardDevice(InputManager.Current)
    {
        protected override KeyStates GetKeyStatesFromSystem(Key key) =>
            ((key is Key.LeftCtrl or Key.RightCtrl) && modifiers.HasFlag(ModifierKeys.Control)) ||
            ((key is Key.LeftAlt or Key.RightAlt) && modifiers.HasFlag(ModifierKeys.Alt)) ||
            ((key is Key.LeftShift or Key.RightShift) && modifiers.HasFlag(ModifierKeys.Shift))
                ? KeyStates.Down : KeyStates.None;
    }
    sealed class OffscreenSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
