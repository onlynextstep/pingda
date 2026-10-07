# StudioDrawing 离屏组件测试

在仓库根目录使用本机 .NET 9 SDK，以 PowerShell 运行：

```powershell
dotnet run --project .\apps\PingXu\tests\PingXu.Studio.Tests\PingXu.Studio.Tests.csproj
```

测试使用 STA、内存假硬件数据和 WPF 软件离屏渲染。项目只引用 Core 模型，并直接编译生产文件 StudioDrawing.cs；不引用 App 或 Windows 硬件后端，不创建 Application、Window 或桌面窗口，不调用 Capture、Validate、Apply、Restore，也不读取或写入用户显示配置。不接受真实硬件模式或其他运行参数。

19 项测试覆盖：真实等比几何及负坐标、旋转后的宽高、真实间距、选择回调及输入不变性、中文外置名称、分辨率与精确比例、世界坐标标尺和网格、酸黄边框与对角方标、可见点阵矢量、十种数字与长编号、主屏文字的有效字号及最终位图墨迹、停用 chip 的回调和滚动条空间、未知硬件信息、空布局和极小/极端尺寸。

已按测试先行执行：组件不存在时最初 16 项均因缺少公共 API 而失败；离屏观察后新增的主屏文字字号与停用条高度测试也分别先出现预期失败，再修正生产代码。

运行时在本测试目录的 artifacts 中生成三张 PNG，均为假硬件预览：

- studio-1100x650.png
- studio-400x300.png
- studio-portrait-400x300.png

## 接入约定

生产入口位于 PingXu.App 命名空间：

```csharp
public static void DrawLayout(
    Canvas canvas,
    DisplayProfile draft,
    IReadOnlyList<DisplayInfo> live,
    IReadOnlyDictionary<string, string> labels,
    IReadOnlyDictionary<string, int> numbers,
    string? selected,
    Action<string> select);
```

在 Canvas 的 UI 线程调用。每次调用替换它的全部 Children，并设置暗底色和 ClipToBounds。主代理在尺寸、草稿、名称、编号或选中状态变化后重新调用；组件不注册尺寸事件。选择只回调传入的原始显示器 ID，后续修改 selected、检查器和重绘由调用方负责。现有的场景提示等外围控件仍由调用方维护。

直接使用草稿 Width/Height/X/Y；宽高应已经包含旋转结果。ID 匹配不区分大小写。优先使用传入编号，缺失时按 live 枚举顺序编号；完全未知的 ID 显示问号，不伪造在线状态。名称优先使用 labels，回退到 live.Name，再回退到 ID。长宽比由实际宽高约分，例如 3440×1440 显示 43 : 18。

400～1100 × 300～650 的正常画布使用精确等比坐标。尺寸较小时先收起分辨率/比例信息，仍绘制编号及完整主屏标记；低于 400×300 时整体等比缩小绘图表面，零尺寸则安全等待下一次调用。极端远距或极小显示器仍保持真实比例，不夸大显示器矩形。

本测试验证独立生产组件；主窗口接入与总 UI 测试由主代理负责。
