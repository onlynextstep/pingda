using PingXu.Core;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

if(args.Length == 2 && args[0] == "--live")
{
 using var client=new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=TimeSpan.FromMinutes(5) };
 var service=new UpdateService(client);
 var endpoint=new Uri("https://raw.githubusercontent.com/onlynextstep/pingda/main/updates/latest.json");
 var update=await service.CheckAsync(endpoint,new Version(0,6,0)) ?? throw new Exception("旧版本未发现更新");
 if(await service.CheckAsync(endpoint,new Version(0,6,1)) != null) throw new Exception("同版本错误通知升级");
 var download=await service.DownloadAsync(update,args[1],null,CancellationToken.None);
 Console.WriteLine("PASS 公开端点发现0.6.1、同版不重复提示、真实下载与SHA256校验");
 Console.WriteLine(download);
 return 0;
}

var bytes = new byte[] { 77, 90, 1, 2, 3 };
var release = new UpdateRelease("0.6.2", "修复屏幕切换", "https://updates.example.com/setup.exe", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
int failures = 0;
await Check("识别渲染线程故障及嵌套异常", () => { RenderFailureTests.Run(); return Task.CompletedTask; });
async Task Check(string name, Func<Task> run) { try { await run(); Console.WriteLine("PASS " + name); } catch(Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
void Assert(bool ok) { if (!ok) throw new Exception("断言失败"); }
async Task Reject(Func<Task> run) { try { await run(); } catch (InvalidDataException) { return; } throw new Exception("应拒绝不安全的更新"); }
await Check("新版本可发现；同版和旧版不提示", async () => {
 using var client = new HttpClient(new Fixture(JsonSerializer.SerializeToUtf8Bytes(release)));
 var service = new UpdateService(client);
 Assert(await service.CheckAsync(new Uri("https://updates.example.com/latest.json"), new Version(0,6,1)) != null);
 Assert(await service.CheckAsync(new Uri("https://updates.example.com/latest.json"), new Version(0,6,2)) == null);
 Assert(await service.CheckAsync(new Uri("https://updates.example.com/latest.json"), new Version(0,7,0)) == null);
});
await Check("拒绝 HTTP 下载地址", () => Reject(() => { UpdateService.Validate(release with { Url="http://updates.example.com/setup.exe" }); return Task.CompletedTask; }));
await Check("拒绝缺失哈希", () => Reject(() => { UpdateService.Validate(release with { Sha256="" }); return Task.CompletedTask; }));
await Check("下载校验并拒绝损坏文件", async () => {
 var folder=Path.Combine(Path.GetTempPath(),"pingda-update-test-"+Guid.NewGuid().ToString("N"));
 try {
  using var client=new HttpClient(new Fixture(bytes)); var service=new UpdateService(client);
  var file=await service.DownloadAsync(release,folder,null,CancellationToken.None);
  Assert(File.ReadAllBytes(file).SequenceEqual(bytes));
  await Reject(()=>service.DownloadAsync(release with { Sha256=new string('0',64) },folder,null,CancellationToken.None));
  Assert(!Directory.EnumerateFiles(folder,"*.partial",SearchOption.AllDirectories).Any());
 } finally { if(Directory.Exists(folder)) Directory.Delete(folder,true); }
});
await Check("拒绝长度不符", async () => {
 using var client=new HttpClient(new Fixture(bytes));
 await Reject(()=>new UpdateService(client).DownloadAsync(release with { Size=4 },Path.GetTempPath(),null,CancellationToken.None));
});
await Check("支持GitHub安装包HTTPS重定向", async () => {
 using var client=new HttpClient(new RedirectFixture(bytes,false));
 var folder=Path.Combine(Path.GetTempPath(),"pingda-redirect-"+Guid.NewGuid().ToString("N"));
 try { var path=await new UpdateService(client).DownloadAsync(release,folder,null,CancellationToken.None); Assert(File.ReadAllBytes(path).SequenceEqual(bytes)); }
 finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
});
await Check("拒绝HTTPS重定向降级HTTP", async () => {
 using var client=new HttpClient(new RedirectFixture(bytes,true));
 await Reject(()=>new UpdateService(client).DownloadAsync(release,Path.GetTempPath(),null,CancellationToken.None));
});
await Check("取消下载不留下未验证文件", async () => {
 using var client=new HttpClient(new Fixture(bytes)); using var cancel=new CancellationTokenSource(); cancel.Cancel();
 var folder=Path.Combine(Path.GetTempPath(),"pingda-cancel-"+Guid.NewGuid().ToString("N"));
 try { await new UpdateService(client).DownloadAsync(release,folder,null,cancel.Token); throw new Exception("取消未生效"); }
 catch(OperationCanceledException) { Assert(!Directory.Exists(folder) || !Directory.EnumerateFiles(folder).Any()); }
 finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
});
return failures == 0 ? 0 : 1;
sealed class Fixture(byte[] body) : HttpMessageHandler {
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new ByteArrayContent(body),RequestMessage=request });
}
sealed class RedirectFixture(byte[] bytes,bool insecure) : HttpMessageHandler {
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
  var result=new HttpResponseMessage(request.RequestUri!.AbsolutePath=="/setup.exe"?HttpStatusCode.Redirect:HttpStatusCode.OK) { RequestMessage=request,Content=new ByteArrayContent(bytes) };
  if(result.StatusCode==HttpStatusCode.Redirect) result.Headers.Location=new Uri((insecure?"http":"https")+"://assets.example.com/binary.exe");
  return Task.FromResult(result);
 }
}
