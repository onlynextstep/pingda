param([Parameter(Mandatory)][string]$Compiler)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$compilerPath=(Resolve-Path -LiteralPath $Compiler).Path
if((& $compilerPath /VERSION) -notmatch 'v3\.12') { throw '需要 NSIS 3.12。' }
Copy-Item -LiteralPath (Join-Path (Split-Path $compilerPath) 'COPYING') -Destination (Join-Path $projectRoot 'distribution/NSIS-LICENSE.txt') -Force
$result=& (Join-Path $PSScriptRoot 'Build-BetaCandidate.ps1') | Select-Object -Last 1
if(!$result.PayloadPath -or !(Test-Path -LiteralPath $result.PayloadPath)) { throw '候选构建未通过。' }
$payload=$result.PayloadPath
$release=Join-Path $projectRoot ('dist/installer-0.6.1-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $release | Out-Null
$work=Join-Path $projectRoot ('artifacts/installer-build-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$deleteList=Join-Path $work 'delete-files.nsh'
$lines=@(Get-ChildItem -LiteralPath $payload -File -Recurse | ForEach-Object {
    $relative=[IO.Path]::GetRelativePath($payload,$_.FullName)
    if($relative -match '["$\r\n]'){throw '文件名不能安全用于安装脚本'}
    'Delete "$INSTDIR\app\'+$relative+'"'
})
$lines+=@(Get-ChildItem -LiteralPath $payload -Directory -Recurse | Sort-Object {$_.FullName.Length} -Descending | ForEach-Object {'RMDir "$INSTDIR\app\'+[IO.Path]::GetRelativePath($payload,$_.FullName)+'"'})
$lines | Set-Content -LiteralPath $deleteList -Encoding utf8BOM
$icon=Join-Path $projectRoot 'src/PingXu.App/Assets/PingXu.ico'
$installerScript=Join-Path $projectRoot 'installer/PingDa.nsi'
$scriptHash=(Get-FileHash $installerScript).Hash
foreach($test in @($false,$true)) {
    $out=if($test){Join-Path $work 'PingDa-InstallerTest.exe'}else{Join-Path $release 'PingDa-0.6.1-Setup.exe'}
    $arguments=@('/V2','/INPUTCHARSET','UTF8',"/DPAYLOAD=$payload","/DOUTPUT=$out","/DICON=$icon","/DDELETE_LIST=$deleteList")
    if($test){$arguments+='/DTEST_BUILD'}
    & $compilerPath @arguments $installerScript
    if($LASTEXITCODE -ne 0){throw '安装包编译失败'}
}
if((Get-FileHash $installerScript).Hash -ne $scriptHash){throw '编译过程中安装脚本发生变化'}
Copy-Item -LiteralPath (Join-Path $projectRoot 'distribution/README.md') -Destination $release
$manifest=[ordered]@{Version='0.6.1';Runtime='9.0.20';Compiler='NSIS 3.12';SourceStamp=$result.SourceStamp;InstallerScriptSHA256=$scriptHash;Payload=$payload;TestInstaller=(Join-Path $work 'PingDa-InstallerTest.exe');Release=$release}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $work 'build.json') -Encoding utf8
Write-Output ($manifest | ConvertTo-Json -Compress)
