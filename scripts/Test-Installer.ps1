param([Parameter(Mandatory)][string]$Installer)
$ErrorActionPreference='Stop'
$installerPath=(Resolve-Path -LiteralPath $Installer).Path
if([IO.Path]::GetFileName($installerPath) -ne 'PingDa-InstallerTest.exe'){throw '仅允许独立验收安装包'}
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot=Join-Path $projectRoot ('artifacts/installer-smoke-'+[guid]::NewGuid().ToString('N'))
$configPath=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PingXu'
function ConfigStamp {
    @(Get-ChildItem -LiteralPath $configPath -Filter '*.json' -File | Sort-Object Name | ForEach-Object {$_.Name+':'+(Get-FileHash $_.FullName).Hash}) -join '|'
}
$before=ConfigStamp
$results=[Collections.Generic.List[string]]::new()
function RunSetup([int]$Expected,[string]$Extra='') {
    $p=Start-Process -FilePath $installerPath -ArgumentList "/S $Extra /D=$testRoot" -WindowStyle Hidden -PassThru -Wait
    if($p.ExitCode -ne $Expected){throw "安装退出码 $($p.ExitCode)，预期 $Expected"}
}
$lock=[Threading.Mutex]::new($false,('Local\PingDa.InstallerTest.Desktop.'+[Environment]::UserName))
try {RunSetup 10; if(Test-Path $testRoot){throw '运行中阻断仍写入文件'}; $results.Add('运行中安装被阻止，未写入目录')} finally {$lock.Dispose()}
RunSetup 0
$exe=Join-Path $testRoot 'app/PingXu.exe'
if(!(Test-Path $exe)){throw '首次安装缺少入口'}
$p=Start-Process -FilePath $exe -ArgumentList '--verify-install' -WindowStyle Hidden -PassThru -Wait
if($p.ExitCode -ne 0){throw '自包含 WPF/图标启动验证失败'}
$runtime=Get-Content (Join-Path $testRoot 'app/PingXu.runtimeconfig.json') -Raw | ConvertFrom-Json
if($runtime.runtimeOptions.frameworks -or $runtime.runtimeOptions.includedFrameworks.Count -ne 2){throw '仍依赖机器全局运行时'}
$results.Add('全新安装、自包含运行时、WPF资源验证通过')
$hash=(Get-FileHash $exe).Hash
RunSetup 0
if((Get-FileHash $exe).Hash -ne $hash){throw '重复升级损坏程序'}
$results.Add('同版本重装升级成功')
RunSetup 20 '/FAILACTIVATE'
if((Get-FileHash $exe).Hash -ne $hash -or (Test-Path (Join-Path $testRoot 'app-previous'))){throw '模拟替换失败后未恢复旧版'}
$results.Add('模拟新版激活失败，旧程序目录和文件恢复成功')
$unknown=Join-Path $testRoot 'app/user-note.txt'
[IO.File]::WriteAllText($unknown,'User file must survive upgrade and uninstall.')
RunSetup 20
if(!(Test-Path $unknown) -or (Get-FileHash $exe).Hash -ne $hash){throw '有未知文件时升级损坏了原目录'}
$results.Add('程序目录存在额外文件时拒绝覆盖，原文件保留')
$uninstaller=Join-Path $testRoot 'app/Uninstall.exe'
$p=Start-Process -FilePath $uninstaller -ArgumentList '/S' -WindowStyle Hidden -PassThru -Wait
if($p.ExitCode -ne 0){throw "卸载返回 $($p.ExitCode)"}
$deadline=[DateTime]::UtcNow.AddSeconds(30)
while((Test-Path $exe) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 200}
if(Test-Path $exe){throw '卸载后仍有主程序'}
if(!(Test-Path $unknown)){throw '卸载误删用户额外文件'}
if(Test-Path 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/PingDa-InstallerTest'){throw '卸载登记未清理'}
if((ConfigStamp) -ne $before){throw '验收安装或卸载改动了用户配置'}
$results.Add('卸载程序及注册项完成，原用户配置逐项哈希保持一致')
$results | ForEach-Object {Write-Output "PASS $_"}
Write-Output "验收目录：$testRoot"
