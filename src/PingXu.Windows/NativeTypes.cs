using System.Runtime.InteropServices;

namespace PingXu.Windows;

// Windows SDK wingdi.h. BOOL is a four-byte int; mode union is eight-byte aligned.
[StructLayout(LayoutKind.Sequential)]
internal struct Luid : IEquatable<Luid>
{
    public uint Low;
    public int High;
    public readonly bool Equals(Luid other) => Low == other.Low && High == other.High;
    public override readonly bool Equals(object? obj) => obj is Luid other && Equals(other);
    public override readonly int GetHashCode() => HashCode.Combine(Low, High);
}
[StructLayout(LayoutKind.Sequential)]
internal struct Rational { public uint Numerator, Denominator; }
[StructLayout(LayoutKind.Sequential)]
internal struct Point { public int X, Y; }
[StructLayout(LayoutKind.Sequential)]
internal struct Region { public uint Width, Height; }
[StructLayout(LayoutKind.Sequential)]
internal struct Rect { public int Left, Top, Right, Bottom; }
[StructLayout(LayoutKind.Sequential)]
internal struct SourceMode { public uint Width, Height, PixelFormat; public Point Position; }
[StructLayout(LayoutKind.Sequential)]
internal struct VideoSignal
{
    public ulong PixelRate;
    public Rational HSync, VSync;
    public Region ActiveSize, TotalSize;
    public uint VideoStandard, ScanLineOrdering;
}
[StructLayout(LayoutKind.Sequential)]
internal struct DesktopImage { public Point PathSourceSize; public Rect ImageRegion, ImageClip; }
[StructLayout(LayoutKind.Explicit, Size = 48)]
internal struct ModeUnion
{
    [FieldOffset(0)] public VideoSignal Target;
    [FieldOffset(0)] public SourceMode Source;
    [FieldOffset(0)] public DesktopImage Desktop;
}
[StructLayout(LayoutKind.Sequential)]
internal struct ModeInfo { public uint Type, Id; public Luid AdapterId; public ModeUnion Data; }
[StructLayout(LayoutKind.Sequential)]
internal struct PathSource { public Luid AdapterId; public uint Id, ModeIndex, StatusFlags; }
[StructLayout(LayoutKind.Sequential)]
internal struct PathTarget
{
    public Luid AdapterId;
    public uint Id, ModeIndex, OutputTechnology, Rotation, Scaling;
    public Rational RefreshRate;
    public uint ScanLineOrdering;
    public int Available;
    public uint StatusFlags;
}
[StructLayout(LayoutKind.Sequential)]
internal struct PathInfo { public PathSource Source; public PathTarget Target; public uint Flags; }
[StructLayout(LayoutKind.Sequential)]
internal struct DeviceInfoHeader { public uint Type, Size; public Luid AdapterId; public uint Id; }
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct SourceDeviceName
{
    public DeviceInfoHeader Header;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string GdiDeviceName;
}
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct TargetDeviceName
{
    public DeviceInfoHeader Header;
    public uint Flags, OutputTechnology;
    public ushort EdidManufactureId, EdidProductCodeId;
    public uint ConnectorInstance;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string MonitorDevicePath;
}
[StructLayout(LayoutKind.Sequential)]
internal struct TargetPreferredMode
{
    public DeviceInfoHeader Header;
    public uint Width, Height;
    public VideoSignal Signal;
}
// The display branch of DEVMODEW's printer/display union occupies bytes 76..91.
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DevMode
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    public ushort SpecVersion, DriverVersion, Size, DriverExtra;
    public uint Fields;
    public Point Position;
    public uint Orientation, FixedOutput;
    public short Color, Duplex, YResolution, TTOption, Collate;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
    public ushort LogPixels;
    public uint BitsPerPel, Width, Height, DisplayFlags, Frequency;
    public uint IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
}
