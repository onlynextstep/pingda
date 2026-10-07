using System.Windows.Media;

namespace PingXu.SceneManager.Tests
{
    internal static class SceneManagerTestRunner
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 0) return 2;
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            int passed = 0, failed = 0;
            void Test(string name, Action action)
            {
                try { action(); passed++; Console.WriteLine("PASS " + name); }
                catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
            }
            SceneManagerTests.Register(Test);
            PingXu.App.Tests.DialogUiTests.Register(Test);
            Console.WriteLine($"Scene manager + existing dialog regression: {passed} passed, {failed} failed. No Application/Window/native display calls; memory-only callbacks.");
            return failed == 0 ? 0 : 1;
        }
    }
}

// Only satisfy unrelated settings method references while compiling the unmodified production Dialogs file.
// The throwing data-directory accessor makes an accidental settings host/file action fail immediately.
namespace PingXu.App
{
    internal static class Program
    {
        public static string DataDirectory => throw new InvalidOperationException("离屏测试不允许访问主程序数据目录。");
    }
    internal static class BrandAssets
    {
        public static string Version => "offline-test";
    }
}
