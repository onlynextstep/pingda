using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using PingXu.Core;
using Button = System.Windows.Controls.Button;

namespace PingXu.App;

public sealed class AppUpdates(Window owner, Func<bool> canInstall, Action<string> install)
{
    // Public release metadata only: no device configuration or user identity is sent.
    public static readonly Uri Endpoint = new("https://raw.githubusercontent.com/onlynextstep/pingda/main/updates/latest.json");
    static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };
    readonly UpdateService service = new(Http);
    bool checking;
    Window? dialog;
    public async Task CheckAsync(bool silent)
    {
        if (dialog != null) { if (!silent) dialog.Activate(); return; }
        if (checking) return;
        if (StoreDistribution.IsPackaged)
        {
            if (!silent) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-windows-store://downloadsandupdates") { UseShellExecute = true });
            return;
        }
        checking = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var release = await service.CheckAsync(Endpoint, typeof(Program).Assembly.GetName().Version!, timeout.Token);
            if (release != null) Show(release);
            else if (!silent) Show(null);
        }
        catch (Exception ex)
        {
            Program.Log(ex);
            if (!silent) Show(null, "暂时无法检查更新，请检查网络后重试。");
        }
        finally { checking = false; }
    }
    void Show(UpdateRelease? release, string? error = null)
    {
        var window = new Window { Owner = owner, Title = "屏搭 · 软件更新", Width = 480, Height = 390,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResize, MinWidth=360, MinHeight=300 };
        window.SetResourceReference(FrameworkElement.StyleProperty, typeof(Window));
        var body = new StackPanel { Margin = new Thickness(26) };
        var title = new TextBlock { Text = release == null ? "检查更新" : $"发现新版本 {release.Version}", FontSize=25, Margin=new Thickness(0,0,0,18) };
        body.Children.Add(title);
        body.Children.Add(new TextBlock { Text=$"当前版本 {BrandAssets.Version}", Margin=new Thickness(0,0,0,14) });
        body.Children.Add(new TextBlock { Text=release?.Notes ?? error ?? "已是最新版本。", TextWrapping=TextWrapping.Wrap });
        var status = new TextBlock { TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,16,0,12) }; body.Children.Add(status);
        var action = new Button { Content=release == null ? "重新检查" : "下载更新", MinHeight=42 };
        action.SetResourceReference(FrameworkElement.StyleProperty,"Primary"); body.Children.Add(action);
        var cancel = new Button { Content="稍后", MinHeight=38, Margin=new Thickness(0,10,0,0) }; body.Children.Add(cancel);
        window.Content = new ScrollViewer { Content=body, VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        CancellationTokenSource? download = null;
        string? package = null;
        cancel.Click += (_,_) => { if(download != null) download.Cancel(); else window.Close(); };
        DependencyPropertyChangedEventHandler ownerVisibilityChanged = (_, _) => { if (!owner.IsVisible) window.Close(); };
        owner.IsVisibleChanged += ownerVisibilityChanged;
        window.Closed += (_,_) => { download?.Cancel(); dialog=null; owner.IsVisibleChanged -= ownerVisibilityChanged; };
        action.Click += async (_,_) =>
        {
            if(release == null) { window.Close(); await CheckAsync(false); return; }
            if(package != null)
            {
                if(!canInstall()) { status.Text="请先结束屏幕切换，并保存或取消当前修改，再安装更新。"; return; }
                action.IsEnabled=false;
                try { await UpdateService.VerifyAsync(package,release); if(!canInstall()) { status.Text="请先结束当前操作。"; return; } install(package); }
                catch(Exception ex) { Program.Log(ex); status.Text="未能启动安装，请重新下载后重试。"; package=null; action.Content="重新下载"; }
                finally { action.IsEnabled=true; }
                return;
            }
            action.IsEnabled=false; cancel.Content="取消下载";
            using var token=new CancellationTokenSource(); download=token;
            try
            {
                package=await service.DownloadAsync(release,Path.Combine(Program.DataDirectory,"updates"),new Progress<int>(p=>status.Text=$"正在下载 {p}%"),token.Token);
                status.Text="下载完成。安装时将退出屏搭，预设和设置会保留。"; action.Content="退出并安装";
            }
            catch(OperationCanceledException) { status.Text="下载已取消。"; }
            catch(Exception ex) { Program.Log(ex); status.Text="下载或校验失败，请重试。"; action.Content="重新下载"; }
            finally { download=null; action.IsEnabled=true; cancel.Content="稍后"; }
        };
        dialog=window; window.Show();
    }
}
