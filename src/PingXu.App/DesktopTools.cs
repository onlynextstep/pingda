using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
namespace PingXu.App;
public static class DesktopTools
{
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr data);
    delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RectNative r);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [StructLayout(LayoutKind.Sequential)] struct RectNative { public int Left, Top, Right, Bottom; }
    public static int RescueWindows()
    {
        int count = 0; var area = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || IsIconic(h) || GetWindowTextLength(h) == 0 || MonitorFromWindow(h, 0) != IntPtr.Zero || !GetWindowRect(h, out var r)) return true;
            if (SetWindowPos(h, IntPtr.Zero, area.Left + 30, area.Top + 30, Math.Clamp(r.Right - r.Left, 200, Math.Max(200, area.Width - 60)), Math.Clamp(r.Bottom - r.Top, 120, Math.Max(120, area.Height - 60)), 0x14)) count++; return true;
        }, IntPtr.Zero); return count;
    }
    public static FrameworkElement CreateIdentificationContent(int number, string name, bool primary)
    {
        var content = new StackPanel();
        var artwork = NumberArtwork.Create(number.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
        artwork.Height = 104; artwork.MaxWidth = 256;
        artwork.Margin = new Thickness(0, 0, 0, 12);
        System.Windows.Automation.AutomationProperties.SetName(artwork, $"显示器编号 {number:00}");
        content.Children.Add(artwork);
        content.Children.Add(new TextBlock { Text = name, FontSize = 16, Foreground = Brushes.White,
            FontFamily = new FontFamily("Microsoft YaHei UI"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        if (primary) content.Children.Add(new TextBlock { Text = "主屏", FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(220, 255, 66)), TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0) });
        return new Border { Padding = new Thickness(22), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 255, 66)),
            Background = new SolidColorBrush(Color.FromRgb(21, 22, 22)), Child = content };
    }

    public static void Identify(IReadOnlyDictionary<string, (int Number, string Name)> labels)
    {
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            // Match by device name, never by the OS enumeration index.
            if (!labels.TryGetValue(screen.DeviceName, out var label)) continue;
            var w = new Window { Title = "屏幕识别", WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize, Width = 320, SizeToContent = SizeToContent.Height,
                Topmost = true, ShowInTaskbar = false, ShowActivated = false,
                Background = new SolidColorBrush(Color.FromRgb(21, 22, 22)),
                Content = CreateIdentificationContent(label.Number, label.Name, screen.Primary) };
            w.SourceInitialized += (_, _) =>
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                SetWindowPos(handle, IntPtr.Zero, screen.WorkingArea.Left + 40, screen.WorkingArea.Top + 40, 0, 0, 0x15);
            };
            w.Show();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) => { timer.Stop(); w.Close(); }; timer.Start();
        }
    }
}
