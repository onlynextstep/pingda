using System.IO;
using System.Text.Json;
using PingXu.Core;
using PingXu.Windows;
using Application = System.Windows.Application;

namespace PingXu.App;
public static class Program
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PingXu");
    [STAThread]
    public static int Main(string[] args)
    {
        // Installer verification: load the bundled WPF runtime/resources without touching user data or displays.
        if (args.SequenceEqual(new[] { "--verify-install" }))
        {
            try
            {
                var root = AppContext.BaseDirectory;
                for (var parent = new DirectoryInfo(root); parent != null; parent = parent.Parent)
                    if (parent.Attributes.HasFlag(FileAttributes.ReparsePoint)) return 3;
                if (Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
                    .Any(path => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))) return 3;
                using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "build-manifest.json")));
                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "build-manifest.json", "Uninstall.exe" };
                foreach (var entry in manifest.RootElement.GetProperty("Files").EnumerateArray())
                {
                    var relative = entry.GetProperty("Path").GetString()!;
                    var path = Path.GetFullPath(Path.Combine(root, relative));
                    if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return 3;
                    known.Add(relative.Replace('/', Path.DirectorySeparatorChar));
                    using var file = File.OpenRead(path);
                    if (!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file)).Equals(entry.GetProperty("SHA256").GetString(), StringComparison.OrdinalIgnoreCase)) return 3;
                }
                if (Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any(path => !known.Contains(Path.GetRelativePath(root,path)))) return 3;
                var probe = new Application();
                probe.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri("Theme.xaml", UriKind.Relative) });
                using var icon = BrandAssets.CreateTrayIcon();
                return icon.Width == 32 ? 0 : 2;
            }
            catch { return 2; }
        }
        Directory.CreateDirectory(DataDirectory);
        try
        {
            using var maintenance = Mutex.TryOpenExisting("Local\\PingDa.Setup", out var setup) ? setup : null;
            if (maintenance != null)
            {
                if (args.Length == 0) System.Windows.MessageBox.Show("屏搭正在安装或卸载，请完成后再打开。", "屏搭");
                return 3;
            }
            if (args.Length == 2 && args[0] == "--guardian") return Guardian.Run(args[1]);
            if (args.Length == 2 && args[0] == "--diagnose") { var s = new WindowsDisplayService().Capture(); ProfileStore.AtomicWrite(args[1], JsonSerializer.Serialize(s, ProfileStore.Json)); return 0; }
            using var mutex = new Mutex(true, "Local\\PingXu.Desktop." + Environment.UserName, out bool first);
            if (!first)
            {
                if (!InstanceActivation.TryActivate(InstanceActivation.Name))
                    System.Windows.MessageBox.Show("屏搭正在启动，请稍后再次打开。", "屏搭");
                return 0;
            }
            // Display topology changes can invalidate the WPF/D3D render channel. This utility
            // does not need GPU rendering; select software before creating any WPF window.
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            var app = new Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri("Theme.xaml", UriKind.Relative) });
            bool reporting = false;
            app.DispatcherUnhandledException += (s, e) =>
            {
                e.Handled = true; // Set before showing any modal UI, which pumps the dispatcher.
                Log(e.Exception);
                if (RenderFailure.IsFatal(e.Exception))
                {
                    // A broken render channel cannot be repaired by dismissing a dialog.
                    // Exit only this UI process; the independent guardian retains recovery ownership.
                    FatalProcessExit.Terminate(2);
                    return;
                }
                if (reporting) return;
                reporting = true;
                try
                {
                    if (app.MainWindow is { } owner)
                        Dialogs.ShowFailure(owner, "操作未完成", "这次操作遇到了错误。", "请关闭提示后重试。", "若再次出现，请反馈错误详情。", e.Exception.ToString());
                    else System.Windows.MessageBox.Show("操作未完成：" + e.Exception.Message, "屏搭");
                }
                finally { reporting = false; }
            };
            var window = new MainWindow(new WindowsDisplayService());
            using var activation = new InstanceActivation(InstanceActivation.Name,
                () => app.Dispatcher.BeginInvoke(new Action(window.OpenWindow)));
            app.Run(window); return 0;
        }
        catch (Exception e) { Log(e); if (args.Length == 0) System.Windows.MessageBox.Show("启动失败：" + e.Message, "屏搭"); return 1; }
    }
    public static void Log(Exception ex) { try { BoundedErrorLog.Append(DataDirectory, ex); } catch { } }
}
