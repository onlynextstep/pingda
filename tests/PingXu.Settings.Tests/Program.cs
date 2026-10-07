using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using PingXu.App;
internal static class Program
{
 [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr handle);
 [STAThread] static int Main()
 {
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
   Console.WriteLine("PASS 主窗口保持启用、设置单实例、收起前台关闭设置、再次打开正常"); return 0;
  } catch(Exception e) { Console.WriteLine(e);return 1; }
  finally {owner.Close(); app.Shutdown();}
 }
}
