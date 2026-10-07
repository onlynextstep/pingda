# 屏序 WPF 只读 UI 冒烟与截图

需要 Windows 桌面环境与 .NET 9 SDK。没有测试框架/NuGet 第三方依赖，是引用真实 App 的 STA console 程序。

从仓库根目录运行 PowerShell：

```powershell
& .\apps\PingXu\tests\PingXu.App.Tests\run.ps1
```

请使用此脚本：它把**所有引用项目**的编译中间文件、输出及 CLI/NuGet 缓存定向到本测试目录下的 `artifacts`，避免普通 `dotnet run` 在 src 产生 bin/obj。进程环境变量在退出时恢复。不安装依赖软件，不修改系统设置。

输出 `artifacts/ui-1400.png`（1400×940）、`artifacts/ui-1060.png`（1060×720）、`artifacts/run-report.md`、构建及测试输出文本。截图为 96 DPI 的真实 MainWindow.Content，不含系统窗口边框；画面明确标注假硬件。

强制本测试进程使用 WPF SoftwareOnly 软件渲染，不依赖真实桌面截图、激活窗口或 GetCursorPos 权限。输出进行颜色采样，拒绝黑屏、透明或单色空白图；这不是视觉质量验收。

别名测试只修改内存 aliases，验证插入次序编号、检查器/画布/场景提示一致性及“命名屏幕”入口存在。深色 ComboBox 测试保持 Popup 关闭。不读写真实 Preferences，不点击命名/识别按钮，不执行持久化逻辑或真实识别浮层。

视觉回归断言：真实 Window 底色匹配 App Bg、非白且文字对比至少 4.5:1；遍历 ApplyButton 视觉树，验证实际 TextBlock 前景继承按钮颜色（值来源 Inherited），并对模板 Chrome 实际底色计算至少 4.5:1 的对比度。截图报告记录颜色与测量值，不以 Button.Foreground 属性本身代替真实文字颜色验证。

隔离方式：公共构造注入会记录并拒绝一切调用的 FakeDisplayService；兼容可选 dataDirectory 参数，并将 ProfileStore 替换为测试目录路径；构造后停止 hardwareTimer；不 Show、不启动消息循环、不触发 Loaded/SourceInitialized，直接反射注入 snapshot/draft/profiles/aliases。主题加载使用 `pack://application:,,,/PingXu;component/Theme.xaml`。内容脱离 Window 后 Measure/Arrange/RenderTargetBitmap。私有方法主要允许预览与布局；Save/Manage 仅允许 profilesReady=false 的前置拒绝；单独的异步入口仅允许 recoveryBlocked=true、busy=false、有效草稿的 ApplyDraft 前置拒绝，通过反射取得 Task 并 await，若任务不是立即完成则失败，不泵送消息循环。不允许 RefreshHardware、Dialogs、DesktopTools 或生产程序入口。没有真实输入模拟或真实硬件测试选项。

测试覆盖场景克隆、三屏参数、旋转、主屏、最后一屏保护以及 busy 状态的数据修改守卫。它不验证 Guardian、热插拔、驱动、系统快捷键、托盘、UI Automation/读屏、实际焦点或跨屏 DPI。截图必须另行视觉检查，不能把 PNG 成功编码当成产品 UI 已验收。反射字段是测试缝，App 改名后应失败并更新测试。
