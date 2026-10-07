using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PingXu.App.Tests;

internal static class StatusFeedbackChecks
{
    public static void Register(Action<string, Action> test)
    {
        test("状态卡靠右收紧，进行中与成功样式切换且长错误不撑满画布", () =>
        {
            using var f = new WindowFixture(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PingXu-notice-unused"));
            void Show(string method, string message) => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(f.Window, [message]);
            var notice = f.Get<Border>("StatusNotice");
            Show("Status", new string('测', 300));
            VisualChecks.Layout(f, 1488, 1020);
            Check(notice.ActualWidth <= 380 && notice.HorizontalAlignment == HorizontalAlignment.Right,
                "状态提示仍横跨画布");
            Show("StatusProgress", "正在切换屏幕…");
            Check(f.Get<ProgressBar>("StatusProgressLine").Visibility == Visibility.Visible, "进行中缺少动态反馈");
            Check(f.Get<TextBlock>("StatusHeading").Text == "正在切换屏幕…", "未呈现简短进度标题");
            SaveCard("progress");
            Show("StatusSuccess", "布局已应用。");
            Check(f.Get<ProgressBar>("StatusProgressLine").Visibility == Visibility.Collapsed, "成功后进度仍在运行");
            Check(f.Get<TextBlock>("StatusHeading").Visibility == Visibility.Collapsed
                && f.Get<TextBlock>("StatusLabel").FontSize >= 14, "成功提示仍重复显示标题或正文层级过低");
            SaveCard("success");
            Show("Status", "请连接预设所需的屏幕。");
            Check(f.Get<TextBlock>("StatusHeading").Text == "需要处理", "待处理消息误报成功");
            SaveCard("attention");
            f.AssertIsolated();
            void SaveCard(string state)
            {
                VisualChecks.Layout(f, 1488, 1020);
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1488, 1020, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(f.Content);
                var bounds = notice.TransformToAncestor(f.Content).TransformBounds(new Rect(notice.RenderSize));
                var cropped = new System.Windows.Media.Imaging.CroppedBitmap(bitmap, new Int32Rect((int)bounds.X, (int)bounds.Y,
                    (int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height)));
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(cropped));
                var folder = System.IO.Path.Combine(AppContext.BaseDirectory, "artifacts");
                System.IO.Directory.CreateDirectory(folder);
                using var file = System.IO.File.Create(System.IO.Path.Combine(folder, "notice-" + state + ".png"));
                encoder.Save(file);
            }
        });
        test("普通状态不会按成功字样自动消失", () =>
        {
            string? visible = null;
            using var feedback = new StatusFeedback(message => visible = message, (_, _) => throw new Exception("普通状态不应启动计时"));
            feedback.Show("布局已应用，但设置保存失败。");
            Check(visible == "布局已应用，但设置保存失败。", "错误提示应持续显示");
        });
        test("显式成功四秒后隐藏，旧计时不清除新状态且关闭释放计时", () =>
        {
            string? visible = null;
            var timers = new List<ManualTimer>();
            using var feedback = new StatusFeedback(message => visible = message, (delay, tick) =>
            {
                Check(delay == TimeSpan.FromSeconds(4), "成功提示应显示四秒");
                var timer = new ManualTimer(tick); timers.Add(timer); return timer;
            });
            feedback.ShowSuccess("完成");
            Check(visible == "完成", "成功提示未显示");
            timers[0].Tick();
            Check(visible == null && timers[0].Disposed, "成功到期应隐藏并释放计时");
            feedback.ShowSuccess("旧成功");
            feedback.Show("正在等待确认");
            timers[1].Tick();
            Check(visible == "正在等待确认" && timers[1].Disposed, "旧计时清除了待处理状态");
            feedback.ShowSuccess("较早成功");
            feedback.ShowSuccess("较新成功");
            timers[2].Tick();
            Check(visible == "较新成功", "旧计时清除了新成功");
            timers[3].Tick();
            Check(visible == null, "新成功未按自身计时隐藏");
            feedback.ShowSuccess("关闭前成功");
            feedback.Dispose();
            timers[4].Tick();
            feedback.Show("关闭后消息");
            feedback.ShowSuccess("关闭后成功");
            Check(visible == "关闭前成功" && timers[4].Disposed && timers.Count == 5, "关闭后仍更新UI或安排计时");
        });
        test("主窗口状态提示连接可见性与真实Dispatcher计时，无需等待", () =>
        {
            using var f = new WindowFixture(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PingXu-status-feedback-unused"));
            using var feedback = f.Get<StatusFeedback>("statusFeedback");
            var notice = f.Get<Border>("StatusNotice");
            var label = f.Get<TextBlock>("StatusLabel");
            Check(notice.Visibility == Visibility.Collapsed, "状态提示初始应折叠");
            void Show(string method, string message) => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(f.Window, [message]);
            (DispatcherTimer Timer, EventHandler Tick) Pending()
            {
                var lease = typeof(StatusFeedback).GetField("pending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(feedback)!;
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                return ((DispatcherTimer)lease.GetType().GetField("timer", flags)!.GetValue(lease)!,
                    (EventHandler)lease.GetType().GetField("handler", flags)!.GetValue(lease)!);
            }
            Show("StatusSuccess", "保存完成");
            var first = Pending();
            Check(notice.Visibility == Visibility.Visible && label.Text == "保存完成" && first.Timer.IsEnabled, "成功提示未显示或未启动计时");
            first.Tick(first.Timer, EventArgs.Empty);
            Check(notice.Visibility == Visibility.Collapsed && label.Text == "" && !first.Timer.IsEnabled, "到期未折叠或计时未停止");
            Show("StatusSuccess", "旧成功");
            var old = Pending();
            Show("Status", "已应用，但保存失败");
            old.Tick(old.Timer, EventArgs.Empty);
            Check(notice.Visibility == Visibility.Visible && label.Text == "已应用，但保存失败" && !old.Timer.IsEnabled, "旧计时覆盖了新错误");
            Show("StatusSuccess", "关闭前成功");
            var closing = Pending();
            // Raise only the managed Closed event; no HWND, native Close, or dispatcher pumping.
            typeof(Window).GetMethod("OnClosed", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(f.Window, [EventArgs.Empty]);
            closing.Tick(closing.Timer, EventArgs.Empty);
            Check(!closing.Timer.IsEnabled && label.Text == "关闭前成功", "关闭生命周期未释放计时或关闭后仍更新UI");
            f.AssertIsolated();
        });
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    // Deliberately allow callbacks after cancellation to simulate an already queued tick.
    sealed class ManualTimer(Action tick) : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Tick() => tick();
        public void Dispose() => Disposed = true;
    }
}
