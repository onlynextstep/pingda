using System.Windows.Media;

namespace PingXu.Drag.Tests;

internal static class DragTestRunner
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 0) return 2;
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        int passed = 0, failed = 0;
        void Test(string name, Action body)
        {
            try { body(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e); }
        }
        LayoutDragTests.Register(Test);
        StudioDragControllerTests.Register(Test);
        Console.WriteLine($"Drag: {passed} passed, {failed} failed. No Application/Window/native backend; fake capture only.");
        return failed == 0 ? 0 : 1;
    }
}
