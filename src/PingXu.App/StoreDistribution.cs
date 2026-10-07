using System.Runtime.InteropServices;
namespace PingXu.App;
internal static class StoreDistribution
{
    public static bool IsPackaged
    {
        get { uint length=0; return GetCurrentPackageFullName(ref length,IntPtr.Zero)==122; }
    }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern int GetCurrentPackageFullName(ref uint length,IntPtr name);
}
