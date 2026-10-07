using PingXu.Core;
using System.Runtime.InteropServices;
internal static class RenderFailureTests
{
    public static void Run()
    {
        if (!RenderFailure.IsFatal(new COMException("render", unchecked((int)0x88980406)))) throw new Exception("未识别渲染线程故障");
        if (!RenderFailure.IsFatal(new InvalidOperationException("wrapped", new COMException("render", unchecked((int)0x88980406))))) throw new Exception("未识别嵌套故障");
        if (RenderFailure.IsFatal(new IOException("disk"))) throw new Exception("普通错误误判为渲染故障");
    }
}
