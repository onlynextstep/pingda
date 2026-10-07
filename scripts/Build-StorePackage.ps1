param(
 [Parameter(Mandatory)][string]$Payload,
 [Parameter(Mandatory)][string]$MakeAppx,
 [Parameter(Mandatory)][string]$PackageName,
 [Parameter(Mandatory)][string]$Publisher,
 [Parameter(Mandatory)][string]$PublisherDisplayName
)
$ErrorActionPreference='Stop'
$pingdaRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$pingdaPayload=(Resolve-Path -LiteralPath $Payload).Path
$pingdaCompiler=(Resolve-Path -LiteralPath $MakeAppx).Path
if(!(Test-Path -LiteralPath (Join-Path $pingdaPayload 'build-manifest.json'))){throw '需要已验证的自包含构建目录'}
if($PackageName -notmatch '^[A-Za-z0-9.-]{3,50}$' -or !$Publisher.StartsWith('CN=')){throw '请使用Partner Center分配的包身份'}
$pingdaStage=Join-Path $pingdaRoot ('artifacts/store-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $pingdaStage 'Assets') -Force | Out-Null
Copy-Item -LiteralPath $pingdaPayload -Destination (Join-Path $pingdaStage 'app') -Recurse
Copy-Item -LiteralPath (Join-Path $pingdaRoot 'src/PingXu.App/Assets/pingxu-icon.png') -Destination (Join-Path $pingdaStage 'Assets/Logo.png')
$pingdaTemplate=Get-Content -LiteralPath (Join-Path $pingdaRoot 'store/AppxManifest.template.xml') -Raw
$pingdaTemplate=$pingdaTemplate.Replace('__PACKAGE_NAME__',[Security.SecurityElement]::Escape($PackageName)).Replace('__PUBLISHER__',[Security.SecurityElement]::Escape($Publisher)).Replace('__PUBLISHER_DISPLAY__',[Security.SecurityElement]::Escape($PublisherDisplayName))
[IO.File]::WriteAllText((Join-Path $pingdaStage 'AppxManifest.xml'),$pingdaTemplate,[Text.UTF8Encoding]::new($false))
$pingdaOutput=Join-Path $pingdaRoot ('dist/store-0.6.1-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $pingdaOutput | Out-Null
$pingdaPackage=Join-Path $pingdaOutput 'PingDa-0.6.1-x64.msix'
& $pingdaCompiler pack /d $pingdaStage /p $pingdaPackage /o
if($LASTEXITCODE -ne 0){throw 'MSIX打包失败'}
Write-Output "未签名MSIX：$pingdaPackage"
Write-Output '此文件尚未通过微软商店认证。提交前需验证真实包身份与打包运行行为。'
