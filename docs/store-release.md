# 微软商店发行准备

免费开源版和商店支持版共享功能。商店说明应明确购买用于支持维护，没有额外功能解锁。当前尚未上架。

## 代码与更新渠道

当前MSIX身份检测会让“检查更新”打开商店下载与更新页面，避免商店包自行下载并执行EXE安装器。独立EXE版使用GitHub发布信息。

MSIX打包需采用Partner Center分配的Package Identity Name、Publisher和Publisher Display Name。身份值不能根据GitHub名称猜测。

`store/AppxManifest.template.xml` 和 `scripts/Build-StorePackage.ps1` 提供打包入口。输入已验证的自包含Payload、Windows SDK的makeappx.exe和商店分配的三个身份值，输出未签名MSIX。开发测试身份不能作为正式上架身份。

## 上架所需材料

- Partner Center账户和“屏搭”的产品身份。
- Windows x64 MSIX、自包含运行时和Full Trust声明。
- 中文介绍、截图、图标、支持入口、隐私说明和定价。
- 检验主进程与guardian启动、数据升级、托盘退出、自定义快捷键与商店更新。
- 登录启动需检查打包环境下的行为，必要时使用StartupTask；桌面版的注册表启动项不能直接当作商店包验收结果。

## 产品说明

建议描述：

> 屏搭免费且开源。微软商店版与免费版功能一致，购买用于支持项目持续开发。

源码：MIT。安装包中的运行时、NSIS和其他第三方许可按原许可保留。

商店审查和签名完成前，不将未签名的本地测试MSIX当作商店成品。发布前在真实打包环境中验证切屏、恢复与启动任务。
