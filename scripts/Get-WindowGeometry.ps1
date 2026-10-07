param([Parameter(Mandatory=$true)][int]$ProcessId)
$ErrorActionPreference = 'Stop'
$target = Get-Process -Id $ProcessId
if ($target.ProcessName -ne 'PingXu') { throw 'Expected PingXu process' }
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WindowGeometry {
 [StructLayout(LayoutKind.Sequential)] public struct R { public int Left,Top,Right,Bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct P { public int X,Y; }
 [StructLayout(LayoutKind.Sequential)] public struct M { public int Size; public R Monitor,Work; public int Flags; }
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr w,out R r);
 [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr w,out R r);
 [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr w,ref P p);
 [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr w,int flags);
 [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m,ref M info);
 [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr w);
 [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 public static object Read(IntPtr w) {
  var old=SetThreadDpiAwarenessContext(new IntPtr(-4));
  try {
   R outer,client; var origin=new P(); var m=new M { Size=Marshal.SizeOf(typeof(M)) };
   if(!GetWindowRect(w,out outer)||!GetClientRect(w,out client)||!ClientToScreen(w,ref origin)||!GetMonitorInfo(MonitorFromWindow(w,2),ref m)) throw new InvalidOperationException("Cannot read window geometry");
   return new { Outer=outer,Client=client,Origin=origin,Monitor=m.Monitor,Work=m.Work,Dpi=GetDpiForWindow(w) };
  } finally { SetThreadDpiAwarenessContext(old); }
 }
}
'@
[WindowGeometry]::Read($target.MainWindowHandle) | ConvertTo-Json -Depth 4
