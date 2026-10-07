using System.Runtime.InteropServices;
using PingXu.Core;
using PingXu.Windows;

internal static class RotationCompatibilityProbe
{
    [DllImport("user32.dll", EntryPoint="ChangeDisplaySettingsExW", CharSet=CharSet.Unicode)]
    private static extern int TestMode(string device, ref DevMode mode, nint hwnd, uint flags, nint param);
    internal static int Run()
    {
        var api=new Win32CcdApi(); var before=api.Read();
        var live=CcdLogic.Describe(before);
        var screen=live.Where(d=>d.Enabled).OrderByDescending(d=>(long)d.Width*d.Height).First();
        var profile=new DisplayProfile("probe","旋转只读对照",live.Select(d=>new DisplayTarget(d.Id,d.Enabled,d.Primary,d.X,d.Y,d.Width,d.Height,d.Rotation,d.RefreshRate)).ToList());
        var original=new DevMode{Size=(ushort)Marshal.SizeOf<DevMode>()};
        if(!NativeMethods.EnumDisplaySettingsEx(screen.DeviceName,uint.MaxValue,ref original,0))throw new Exception("Enum current failed");
        Console.WriteLine($"DEVMODE size={original.Size} fields={original.Fields:X} desktop={original.Width}x{original.Height} orientation={original.Orientation}");
        foreach(uint angle in new uint[]{0,1,2,3})
        {
            var dm=original;
            if(angle%2!=dm.Orientation%2)(dm.Width,dm.Height)=(dm.Height,dm.Width);
            dm.Orientation=angle;dm.Fields|=0x180080;
            Console.WriteLine($"CDS_TEST {angle*90}deg {dm.Width}x{dm.Height}: {TestMode(screen.DeviceName,ref dm,0,2,0)}");
            var targets=profile.Displays.Select(d=>d.Id==screen.Id?d with{Rotation=(int)angle*90,Width=(int)dm.Width,Height=(int)dm.Height}:d).ToList();
            var plan=CcdLogic.Build(before,profile with{Displays=targets});
            Console.WriteLine($"SDC_VALIDATE strict {angle*90}: {api.Set(plan,0x60)}");
            Console.WriteLine($"SDC_VALIDATE with solver {angle*90}: {NativeMethods.SetDisplayConfig((uint)plan.Paths.Length,plan.Paths,(uint)plan.Modes.Length,plan.Modes,0x460)}");
        }
        var after=api.Read();
        if(!SnapshotCodec.Bytes(before.Paths).SequenceEqual(SnapshotCodec.Bytes(after.Paths))||!SnapshotCodec.Bytes(before.Modes).SequenceEqual(SnapshotCodec.Bytes(after.Modes)))throw new Exception("Native state changed");
        Console.WriteLine("Native state unchanged. No Apply/Restore.");return 0;
    }
}
