using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Diagnostics;
using PingXu.App;
internal static class Program
{
 [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr handle);
 [STAThread] static int Main(string[] args)
 {
  if (args.Contains("--fatal-exit-probe"))
  {
   AppDomain.CurrentDomain.ProcessExit += (_,_) => Thread.Sleep(Timeout.Infinite);
   FatalProcessExit.Terminate(2);
   return 99;
  }
  using (var activated = new ManualResetEvent(false))
  {
   var name = "Local\\PingDa.TestActivation." + Guid.NewGuid().ToString("N");
   using (var listener = new InstanceActivation(name, () => activated.Set()))
   {
    if (!InstanceActivation.TryActivate(name) || !activated.WaitOne(2000)) throw new Exception("再次启动未唤回已有实例");
    activated.Reset();
    if (!InstanceActivation.TryActivate(name) || !activated.WaitOne(2000)) throw new Exception("重复唤回失败");
   }
   if (InstanceActivation.TryActivate(name)) throw new Exception("退出后仍残留激活入口");
  }
  using (var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--fatal-exit-probe") { UseShellExecute=false, CreateNoWindow=true })!)
  {
   if (!child.WaitForExit(5000)) { child.Kill(); throw new Exception("故障退出被清理事件卡住"); }
   if (child.ExitCode != 2) throw new Exception("故障退出代码错误");
  }
  Console.WriteLine("PASS 故障进程直接结束，不等待退出回调；重复启动唤回已有实例");
  var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
  app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("pack://application:,,,/PingXu;component/Theme.xaml") });
  var owner=new Window { Width=400,Height=300,ShowInTaskbar=false,Left=-20000,Top=-20000 };
  try {
   owner.Show();
   Dialogs.Settings(owner,false,_=>{},()=>{});
   if(!IsWindowEnabled(new WindowInteropHelper(owner).Handle)) throw new Exception("设置禁用了主窗口");
   Dialogs.Settings(owner,false,_=>{},()=>{});
   if(owner.OwnedWindows.Count!=1) throw new Exception("重复打开产生多个设置窗口");
   owner.Hide();
   if(owner.OwnedWindows.Count!=0) throw new Exception("主窗口隐藏后设置仍残留");
   owner.Show(); Dialogs.Settings(owner,false,_=>{},()=>{});
   if(owner.OwnedWindows.Count!=1) throw new Exception("无法再次打开设置");
   owner.Hide(); owner.Show();
   Dialogs.ShowFailure(owner,"切换未完成","未能切换","原布局未变","请重试","test");
   if(!IsWindowEnabled(new WindowInteropHelper(owner).Handle)) throw new Exception("错误提示禁用了主窗口");
   Dialogs.ShowFailure(owner,"切换未完成","未能切换","原布局未变","请重试","test");
   if(owner.OwnedWindows.Count!=1) throw new Exception("错误提示重复弹出");
   owner.Hide();
   if(owner.OwnedWindows.Count!=0) throw new Exception("收起前台后错误提示仍残留");
   Console.WriteLine("PASS 错误提示不锁主窗口、不重复弹出、收起前台时关闭");
   Console.WriteLine("PASS 主窗口保持启用、设置单实例、收起前台关闭设置、再次打开正常"); return 0;
  } catch(Exception e) { Console.WriteLine(e);return 1; }
  finally {owner.Close(); app.Shutdown();}
 }
}
