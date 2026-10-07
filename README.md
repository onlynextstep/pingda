# 屏搭 · 多屏工作空间

炭黑与荧光黄的 Windows 多屏布局工具。拖动排列显示器，保存常用预设，用快捷键或托盘菜单切换。

屏搭免费且开源。计划中的微软商店版与免费版功能一致，购买用于支持项目持续开发。商店版本尚未上架。

## 当前版本

0.6.1 为内测版。支持 Windows 10 / 11 x64、最多六块已连接屏幕、最多100个预设。不同显卡与显示器的兼容性仍需实机验证。

安装程序放在 GitHub Releases，不需要自行编译。旧内部名称 `PingXu` 用于程序集和本地数据兼容，对外名称统一为“屏搭”。

## 功能

- 画布拖动排列，调整横竖方向、分辨率、刷新率、主屏和启用状态。
- 新建与编辑预设；点击即反馈，成功切换后才标记“当前”。
- 自定义全局快捷键和托盘切换。
- 智能确认、倒计时与独立恢复进程。
- 配置备份、诊断导出和找回屏幕外的窗口。
- 检查新版，选择下载、取消或退出并安装；安装包经大小与SHA256校验。

预设与设置留在当前用户的 `%LOCALAPPDATA%\PingXu`，卸载保留这些文件。软件不自动上传屏幕信息。更新检查会访问公开发布地址。

关闭主窗口会收起到托盘，右键托盘可退出。设置面板不会锁住主窗口。

## 从源码运行

需要 Windows 和 .NET 9 SDK。项目根目录执行 PowerShell：

```powershell
dotnet run --project .\src\PingXu.App -c Release
.\scripts\Test-All.ps1
```

构建自包含安装程序还需要 NSIS 3.12：

```powershell
$build = .\scripts\Build-Installer.ps1 -Compiler 'C:\Program Files (x86)\NSIS\makensis.exe' | Select-Object -Last 1 | ConvertFrom-Json
.\scripts\Test-Installer.ps1 -Installer $build.TestInstaller
```

测试安装器使用独立目录与登记项，不改变个人预设。真实显示器切换不能只靠自动测试验证。

## 已知限制

支持本机扩展桌面；不调整HDR、Windows缩放、亮度、ICC和音频。不支持把停用输出当作切断显示器电源。驱动故障、物理断电或恢复进程被结束时，无法保证恢复成功。

恢复状态未知时先在 Windows 显示设置中检查画面，再使用屏搭的“检查恢复保护”。不要直接删除恢复标记。

## 反馈与贡献

在仓库 Issues 提交问题，说明软件版本、Windows版本、显卡与屏幕连接方式、操作步骤。诊断包先检查内容，再决定是否公开上传。私密内容不要发到公开Issues。

贡献前参阅 [CONTRIBUTING.md](CONTRIBUTING.md)。源码使用 [MIT License](LICENSE)，组件说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
