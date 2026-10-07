using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using PingXu.Core;

namespace PingXu.App.Tests;

public static class DialogUiTests
{
    // Register with the existing runner: DialogUiTests.Register(Test). No WindowFixture is needed.
    public static void Register(Action<string, Action> test)
    {
        void Case(string name, Action body) => test("Dialog UI：" + name, () =>
        {
            int windows = Application.Current?.Windows.Count ?? 0;
            body();
            Check((Application.Current?.Windows.Count ?? 0) == windows, "控件工厂创建了 Window");
        });
        Case("错误详情默认折叠、原文可复制、关闭不重试", () =>
        {
            int closes = 0;
            const string details = "检查失败（Windows 错误 5）\n原始错误完整保留";
            var root = Dialogs.CreateFailureContent("未能切换屏幕", "Windows 拒绝了这次操作，具体原因尚未确定。",
                "本次没有更改屏幕布局。", "请刷新显示器后重试。", details, () => closes++);
            var expand = Find<Expander>(root, "FailureDetails");
            var text = Find<TextBox>(root, "FailureDetailsText");
            Check(!expand.IsExpanded && text.IsReadOnly && text.Text == details, "错误详情丢失或默认展开");
            expand.IsExpanded = true;
            text.SelectAll(); Check(text.SelectedText == details, "错误详情无法选中复制");
            Click(Find<Button>(root, "FailureClose")); Check(closes == 1, "关闭回调次数不正确");
        });
        Case("错误提示窄窗口展开详情可滚动，长文本不横向溢出", () =>
        {
            var root = Dialogs.CreateFailureContent("未能切换屏幕", "Windows 拒绝了这次操作，具体原因尚未确定。",
                "本次没有更改屏幕布局。", "请点击刷新显示器后重试。", new string('长', 2000), () => { });
            Find<Expander>(root, "FailureDetails").IsExpanded = true;
            CheckButtons(root, new Size(330, 260), "FailureClose");
            CheckTextFits(Find<TextBlock>(root, "FailureExplanation"), (FrameworkElement)((ScrollViewer)root).Content);
        });
        Case("旧调用签名保持兼容", () =>
        {
            var settings = typeof(Dialogs).GetMethod(nameof(Dialogs.Settings))!;
            Check(settings.GetParameters().Length >= 7 && settings.GetParameters().Skip(4).All(p => p.IsOptional), "Settings四参数调用不再兼容");
            Check(typeof(Dialogs).GetMethod(nameof(Dialogs.ConfirmDisplay), [typeof(Window), typeof(Action), typeof(Action), typeof(TextBlock).MakeByRefType()]) != null,
                "旧确认窗口签名缺失");
            foreach (var callback in new[] { typeof(Action<bool>), typeof(Action<bool, bool>) })
                Check(typeof(Dialogs).GetMethod(nameof(Dialogs.ConfirmDisplay), [typeof(Window), callback, typeof(Action),
                    typeof(TextBlock).MakeByRefType(), typeof(int), typeof(bool)]) != null, "确认窗口回调签名缺失：" + callback);
        });
        Case("三档及20/30/60秒正确回填且不保存", () =>
        {
            foreach (var mode in Enum.GetValues<ConfirmationMode>())
            foreach (int seconds in new[] { 20, 30, 60 })
            {
                int saves = 0;
                var root = Settings(new(mode, seconds), _ => saves++);
                var choices = new[] { Find<RadioButton>(root, "ConfirmSmart"), Find<RadioButton>(root, "ConfirmAlways"), Find<RadioButton>(root, "ConfirmNever") };
                Check(choices.Count(r => r.IsChecked == true) == 1 && choices.Single(r => r.IsChecked == true).Tag is ConfirmationMode selected && selected == mode,
                    "确认模式未唯一回填");
                var timeout = Find<ComboBox>(root, "ConfirmationTimeout");
                Check(timeout.Items.Cast<ComboBoxItem>().Select(i => (int)i.Tag).SequenceEqual(new[] { 20, 30, 60 }), "倒计时档位不正确");
                Check((int)((ComboBoxItem)timeout.SelectedItem).Tag == seconds && saves == 0, "回填修改了配置");
            }
        });
        Case("选择仅改草稿，明确保存才回调一次", () =>
        {
            var saved = new List<ConfirmationOptions>();
            var root = Settings(null, saved.Add);
            Select(Find<RadioButton>(root, "ConfirmAlways"));
            var timeout = Find<ComboBox>(root, "ConfirmationTimeout"); timeout.SelectedIndex = 2;
            Check(saved.Count == 0, "选项变化立即持久化");
            Click(Find<Button>(root, "SaveConfirmation"));
            Check(saved.SequenceEqual(new[] { new ConfirmationOptions(ConfirmationMode.Always, 60) }), "保存回调内容或次数不正确");
            Check(Find<TextBlock>(root, "SettingsStatus").Text.Contains("已保存"), "未反馈保存成功");
        });
        Case("Never常驻风险、不丢超时值且不额外确认", () =>
        {
            var saved = new List<ConfirmationOptions>();
            var root = Settings(new(ConfirmationMode.Smart, 30), saved.Add);
            Select(Find<RadioButton>(root, "ConfirmNever"));
            var risk = Find<TextBlock>(root, "NeverConfirmationRisk");
            Check(risk.Visibility == Visibility.Visible && risk.Text.Contains("即使画面异常")
                && risk.Text.Contains("关闭倒计时") && risk.Text.Contains("失败时仍会尝试恢复"), "风险说明缺失");
            Check(!Find<ComboBox>(root, "ConfirmationTimeout").IsEnabled && saved.Count == 0, "Never选择产生了副作用");
            Click(Find<Button>(root, "SaveConfirmation"));
            Check(saved.Single() == new ConfirmationOptions(ConfirmationMode.Never, 30) && risk.Visibility == Visibility.Visible, "Never保存后丢失风险或倒计时配置");
            Select(Find<RadioButton>(root, "ConfirmSmart"));
            Check(Find<ComboBox>(root, "ConfirmationTimeout").IsEnabled, "返回智能模式未恢复倒计时编辑");
        });
        Case("清除信任独立回调且不保存未提交草稿", () =>
        {
            int clears = 0, saves = 0;
            var root = Settings(null, _ => saves++, () => clears++);
            Select(Find<RadioButton>(root, "ConfirmAlways"));
            var clear = Find<Button>(root, "ClearTrustedProfiles");
            Check(Equals(clear.Content, "重置确认记录") && clear.ToolTip.ToString()!.Contains("不删除预设"), "重置文案不符"); Click(clear);
            Check(clears == 1 && saves == 0 && Find<RadioButton>(root, "ConfirmAlways").IsChecked == true, "清除信任污染确认草稿");
        });
        Case("保存失败可重试且不误报成功", () =>
        {
            int calls = 0;
            var root = Settings(null, _ => { if (++calls == 1) throw new InvalidOperationException("模拟保存失败"); });
            var save = Find<Button>(root, "SaveConfirmation"); Click(save);
            Check(calls == 1 && save.IsEnabled && Find<TextBlock>(root, "SettingsStatus").Text.Contains("模拟保存失败"), "保存失败未恢复交互");
            Click(save); Check(calls == 2 && Find<TextBlock>(root, "SettingsStatus").Text.Contains("已保存"), "无法重试保存");
        });
        Case("旧入口无保存回调时明确禁用，多个工厂不串组", () =>
        {
            var readOnly = Settings();
            Check(!Find<Button>(readOnly, "SaveConfirmation").IsEnabled && !Find<Button>(readOnly, "ClearTrustedProfiles").IsEnabled, "缺少回调却可保存/清除");
            var first = Settings(null, _ => { }); var second = Settings(null, _ => { });
            Select(Find<RadioButton>(first, "ConfirmNever"));
            Check(Find<RadioButton>(second, "ConfirmSmart").IsChecked == true, "两个设置面板的Radio组互相影响");
        });
        Case("确认倒计时与默认记住、取消记住均传给keep", () =>
        {
            foreach (int seconds in new[] { 20, 30, 60 })
            foreach (bool rememberValue in new[] { true, false })
            {
                var kept = new List<bool>(); int cancels = 0;
                var root = Dialogs.CreateConfirmDisplayContent(kept.Add, () => cancels++, out var countdown, seconds, true, out var closing);
                var remember = Find<CheckBox>(root, "RememberProfile");
                Check(remember.Visibility == Visibility.Visible && remember.IsChecked == true && countdown.Text.StartsWith(seconds + " 秒"), "默认记忆/倒计时错误");
                if (!rememberValue) ((IToggleProvider)new CheckBoxAutomationPeer(remember).GetPattern(PatternInterface.Toggle)!).Toggle();
                var keep = Find<Button>(root, "KeepDisplay"); Click(keep); Click(keep); closing();
                Check(kept.SequenceEqual(new[] { rememberValue }) && cancels == 0 && !keep.IsEnabled, "keep参数或决策幂等性错误");
            }
        });
        Case("不可记住时隐藏选项且keep始终false", () =>
        {
            bool? kept = null;
            var root = Dialogs.CreateConfirmDisplayContent(value => kept = value, () => { }, out _, 20, false, out _);
            var remember = Find<CheckBox>(root, "RememberProfile"); Check(remember.Visibility == Visibility.Collapsed, "不可记住时仍显示checkbox");
            remember.IsChecked = true; Click(Find<Button>(root, "KeepDisplay")); Check(kept == false, "隐藏checkbox绕过canRemember");
        });
        Case("未决定关闭与恢复按钮各只取消一次", () =>
        {
            int cancels = 0, closes = 0;
            Dialogs.CreateConfirmDisplayContent(_ => throw new Exception("不应保留"), () => cancels++, out _, 30, true, out var closing);
            closing(); closing(); Check(cancels == 1, "关闭未确认窗口没有只取消一次");
            cancels = 0;
            var root = Dialogs.CreateConfirmDisplayContent(_ => throw new Exception("不应保留"), () => cancels++, out _, 30, true, out closing, () => closes++);
            Click(Find<Button>(root, "RevertDisplay")); closing();
            Check(cancels == 1 && closes == 1, "恢复按钮和Closing重复取消");
        });
        Case("keep失败后仍可关闭取消", () =>
        {
            int cancels = 0;
            var root = Dialogs.CreateConfirmDisplayContent(_ => throw new InvalidOperationException("模拟确认失败"), () => cancels++, out _, 60, true, out var closing);
            var keep = Find<Button>(root, "KeepDisplay"); Click(keep);
            Check(keep.IsEnabled && Find<TextBlock>(root, "ConfirmationStatus").Text.Contains("模拟确认失败"), "keep失败被当成已决定");
            closing(); Check(cancels == 1, "keep失败后关闭不再取消");
        });
        Case("全局不询问默认关闭、始终可见且仅保留提交", () =>
        {
            foreach (int seconds in new[] { 20, 30, 60 })
            foreach (bool canRemember in new[] { false, true })
            foreach (bool profileValue in new[] { false, true })
            foreach (bool globalValue in new[] { false, true })
            {
                var kept = new List<(bool remember, bool dontAskAgain)>(); int cancels = 0;
                var root = Dialogs.CreateConfirmDisplayContent((remember, global) => kept.Add((remember, global)),
                    () => cancels++, out var countdown, seconds, canRemember, out var closing);
                var profile = Find<CheckBox>(root, "RememberProfile");
                var global = Find<CheckBox>(root, "DontAskAgain");
                Check(global.Visibility == Visibility.Visible && global.IsChecked == false && global.IsEnabled,
                    "全局选项未始终显示或默认开启");
                Check(((TextBlock)global.Content).Text == "以后不再询问，直接切换", "全局选项文案不符");
                Check(profile.IsChecked == canRemember && profile.IsEnabled == canRemember, "预设记忆默认值不符");
                var risk = Find<TextBlock>(root, "DontAskAgainRisk");
                Check(risk.Visibility == Visibility.Visible && risk.Text == "关闭后，画面异常也不会因等待超时而自动恢复。程序发现切换失败时仍会尝试恢复原布局。",
                    "风险说明未常驻或文案不符");
                profile.IsChecked = profileValue;
                if (globalValue) Toggle(global);
                Check(profile.IsEnabled == (canRemember && !globalValue), "全局选项未禁用预设记忆");
                Check(kept.Count == 0 && cancels == 0 && countdown.Text.StartsWith(seconds + " 秒"), "勾选提前提交或改变本次倒计时");
                var keep = Find<Button>(root, "KeepDisplay"); Click(keep); Click(keep); closing();
                Check(kept.SequenceEqual(new[] { (canRemember && profileValue && !globalValue, globalValue) }) && cancels == 0,
                    "双bool提交值、全局优先或幂等性错误");
            }
        });
        Case("取消全局勾选恢复原预设选择，不提前提交", () =>
        {
            foreach (bool profileValue in new[] { false, true })
            {
                var kept = new List<(bool, bool)>();
                var root = Dialogs.CreateConfirmDisplayContent((remember, global) => kept.Add((remember, global)),
                    () => { }, out _, 20, true, out _);
                var profile = Find<CheckBox>(root, "RememberProfile"); profile.IsChecked = profileValue;
                var global = Find<CheckBox>(root, "DontAskAgain"); Toggle(global); Toggle(global);
                Check(profile.IsEnabled && profile.IsChecked == profileValue && kept.Count == 0, "取消全局勾选丢失预设草稿或提前提交");
                Click(Find<Button>(root, "KeepDisplay"));
                Check(kept.SequenceEqual(new[] { (profileValue, false) }), "取消全局勾选仍提交全局关闭");
            }
        });
        Case("勾选全局后取消或关闭均不提交keep", () =>
        {
            foreach (bool viaButton in new[] { false, true })
            {
                int keeps = 0, cancels = 0, closes = 0;
                var root = Dialogs.CreateConfirmDisplayContent((_, _) => keeps++, () => cancels++, out _, 30, true,
                    out var closing, () => closes++);
                Toggle(Find<CheckBox>(root, "DontAskAgain"));
                if (viaButton) Click(Find<Button>(root, "RevertDisplay"));
                closing(); closing(); Click(Find<Button>(root, "KeepDisplay"));
                Check(keeps == 0 && cancels == 1 && closes == (viaButton ? 1 : 0), "取消/关闭错误提交全局草稿或重复取消");
            }
        });
        Case("双bool保留失败恢复编辑并维持全局优先", () =>
        {
            int calls = 0;
            var root = Dialogs.CreateConfirmDisplayContent((remember, global) =>
            {
                Check(!remember && global, "失败重试丢失全局优先");
                if (++calls == 1) throw new InvalidOperationException("模拟提交失败");
            }, () => { }, out _, 20, true, out _);
            var global = Find<CheckBox>(root, "DontAskAgain"); Toggle(global);
            var keep = Find<Button>(root, "KeepDisplay"); Click(keep);
            Check(global.IsEnabled && keep.IsEnabled && !Find<CheckBox>(root, "RememberProfile").IsEnabled
                && Find<TextBlock>(root, "ConfirmationStatus").Text.Contains("模拟提交失败"), "失败未恢复编辑或预设被错误解禁");
            Click(keep); Check(calls == 2 && !global.IsEnabled, "重试未完成");
        });
        Case("全局选项及风险文字窄屏完整换行且可滚动到按钮", () =>
        {
            foreach (bool canRemember in new[] { false, true })
            foreach (var size in new[] { new Size(320, 220), new Size(550, 470) })
            {
                var root = Dialogs.CreateConfirmDisplayContent((_, _) => { }, () => { }, out _, 60, canRemember, out _);
                CheckButtons(root, size, "DontAskAgain", "DontAskAgainRisk", "KeepDisplay", "RevertDisplay");
                var global = Find<CheckBox>(root, "DontAskAgain");
                CheckTextFits((TextBlock)global.Content, global);
                if (canRemember)
                {
                    var profile = Find<CheckBox>(root, "RememberProfile");
                    CheckTextFits((TextBlock)profile.Content, profile);
                }
                CheckTextFits(Find<TextBlock>(root, "DontAskAgainRisk"), (FrameworkElement)((ScrollViewer)root).Content);
            }
        });
        Case("小尺寸可滚动到操作按钮，不水平溢出", () =>
        {
            var settings = Settings(new(ConfirmationMode.Never, 60), _ => { }, () => { });
            Check(Descendants(settings).OfType<TextBlock>().Any(t => t.Text == $"屏搭 {BrandAssets.Version} · 本地预览版"), "版本文案未使用程序集版本");
            foreach (var size in new[] { new Size(360, 320), new Size(600, 700) })
                CheckButtons(settings, size, "SaveConfirmation", "ClearTrustedProfiles");
            var confirm = Dialogs.CreateConfirmDisplayContent(_ => { }, () => { }, out _, 60, true, out _);
            foreach (var size in new[] { new Size(320, 220), new Size(550, 390) })
                CheckButtons(confirm, size, "KeepDisplay", "RevertDisplay");
        });
        Case("非法倒计时拒绝且无外部调用", () =>
        {
            int calls = 0; bool rejected = false;
            try { Dialogs.CreateConfirmDisplayContent(_ => calls++, () => calls++, out _, 25, true, out _); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected && calls == 0, "非法倒计时未在创建控件前拒绝");
        });
    }

    private static FrameworkElement Settings(ConfirmationOptions? options = null, Action<ConfirmationOptions>? save = null, Action? clear = null)
        => Dialogs.CreateSettingsContent(false, _ => throw new Exception("不应调用快捷键设置"), () => throw new Exception("不应解除恢复锁定"),
            false, _ => throw new Exception("不应写启动项"), () => throw new Exception("不应打开日志"), options, save, clear);

    private static void Select(RadioButton radio) => ((ISelectionItemProvider)new RadioButtonAutomationPeer(radio).GetPattern(PatternInterface.SelectionItem)!).Select();
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Toggle(CheckBox checkbox) => ((IToggleProvider)new CheckBoxAutomationPeer(checkbox).GetPattern(PatternInterface.Toggle)!).Toggle();
    private static T Find<T>(DependencyObject root, string id) where T : FrameworkElement => Descendants(root).OfType<T>()
        .Single(e => AutomationProperties.GetAutomationId(e) == id);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Descendants(child)) yield return nested;
    }
    private static void Layout(FrameworkElement root, Size size)
    { root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout(); Check(PresentationSource.FromVisual(root) == null, "意外连接桌面窗口"); }
    private static void CheckButtons(FrameworkElement root, Size size, params string[] ids)
    {
        var scroll = (ScrollViewer)root;
        Layout(root, size);
        foreach (string id in ids)
        {
            var button = Find<FrameworkElement>(root, id);
            double top = button.TransformToAncestor((FrameworkElement)scroll.Content).Transform(new Point()).Y;
            scroll.ScrollToVerticalOffset(top); Layout(root, size);
            var bounds = button.TransformToAncestor(scroll).TransformBounds(new Rect(button.RenderSize));
            Check(bounds.Top >= -.1 && bounds.Bottom <= scroll.ActualHeight + .1 && bounds.Left >= -.1 && bounds.Right <= scroll.ViewportWidth + .1,
                $"{id} 在 {size} 无法完整滚动显示：{bounds} / 视口{scroll.ViewportWidth}，偏移{scroll.VerticalOffset}");
            Check(scroll.ScrollableWidth < .1, "对话内容水平溢出");
        }
    }
    private static void CheckTextFits(TextBlock text, FrameworkElement container)
    {
        Check(text.ActualWidth > 0 && text.ActualHeight > 0 && text.TextWrapping == TextWrapping.Wrap, "文字未布局或不换行");
        var probe = new TextBlock { Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize,
            FontStyle = text.FontStyle, FontWeight = text.FontWeight, FontStretch = text.FontStretch,
            TextWrapping = TextWrapping.Wrap, UseLayoutRounding = text.UseLayoutRounding };
        probe.Measure(new Size(text.ActualWidth, double.PositiveInfinity));
        Check(probe.DesiredSize.Height <= text.ActualHeight + .1, "文字高度被裁剪：" + text.Text);
        var bounds = text.TransformToAncestor(container).TransformBounds(new Rect(text.RenderSize));
        Check(bounds.Left >= -.1 && bounds.Right <= container.ActualWidth + .1
            && bounds.Top >= -.1 && bounds.Bottom <= container.ActualHeight + .1,
            $"文字超出所属控件：{text.Text}，{bounds} / {container.RenderSize}");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

#if DIALOG_UI_TEST_STANDALONE
    [STAThread]
    public static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/PingXu;component/Theme.xaml") });
        int passed = 0, failed = 0;
        Register((name, body) => { try { body(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.GetBaseException().Message); } });
        Console.WriteLine($"Dialog UI: {passed} passed, {failed} failed; Windows={app.Windows.Count}; memory callbacks only.");
        return failed == 0 ? 0 : 1;
    }
#endif
}
