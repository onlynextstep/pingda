param([ValidatePattern('^9\.0\.\d+$')][string]$RuntimeVersion='9.0.20')
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
function Get-SourceStamp {
    $files=@(foreach($folder in @('src','tests','scripts','distribution','installer')) {
        Get-ChildItem -LiteralPath (Join-Path $projectRoot $folder) -File -Recurse |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj|artifacts)[\\/]' }
    })
    $lines=@($files | Sort-Object FullName | ForEach-Object {
        [IO.Path]::GetRelativePath($projectRoot,$_.FullName)+' '+(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    })
    $sha=[Security.Cryptography.SHA256]::Create()
    try { return [Convert]::ToHexString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))) }
    finally { $sha.Dispose() }
}
$stamp=Get-SourceStamp
$tests=@('PingXu.Update.Tests','PingXu.Settings.Tests','PingXu.Tests','PingXu.Log.Tests','PingXu.App.Tests','PingXu.Windows.Tests','PingXu.Studio.Tests','PingXu.Drag.Tests','PingXu.SceneManager.Tests')
foreach($test in $tests) {
    & dotnet run --project (Join-Path $projectRoot ('tests/'+$test)) -c Release
    if($LASTEXITCODE -ne 0) { throw "测试失败：$test。未生成候选 ZIP。" }
}
$work=Join-Path $projectRoot ('artifacts/beta-build-'+[guid]::NewGuid().ToString('N'))
$stage=Join-Path $work 'PingXu'
& dotnet publish (Join-Path $projectRoot 'src/PingXu.App') -c Release -r win-x64 --self-contained true "-p:RuntimeFrameworkVersion=$RuntimeVersion" -p:DebugType=None -p:DebugSymbols=false -o $stage
if($LASTEXITCODE -ne 0) { throw '发布构建失败，未生成 ZIP。' }
if((Get-SourceStamp) -ne $stamp) { throw '测试或构建期间源文件发生变化，请结束并行编辑后重新打包。' }
$runtime=Get-Content -LiteralPath (Join-Path $stage 'PingXu.runtimeconfig.json') -Raw | ConvertFrom-Json
if($runtime.runtimeOptions.frameworks -or @($runtime.runtimeOptions.includedFrameworks).Count -ne 2 -or
   @($runtime.runtimeOptions.includedFrameworks | Where-Object version -ne $RuntimeVersion).Count -gt 0) { throw '运行时配置不是预期的自包含版本。' }
foreach($name in @('PingXu.exe','PingXu.dll','coreclr.dll','hostfxr.dll')) {
    if(!(Test-Path -LiteralPath (Join-Path $stage $name))) { throw "缺少运行文件：$name" }
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'distribution/README.md') -Destination (Join-Path $stage 'README.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $stage 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination $stage
Copy-Item -LiteralPath (Join-Path $projectRoot 'distribution/NSIS-LICENSE.txt') -Destination (Join-Path $stage 'NSIS-LICENSE.txt')
$nuget=(& dotnet nuget locals global-packages --list) -replace '^global-packages:\s*',''
if($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $nuget)) { throw '无法定位运行时许可文件。' }
$notices=Join-Path $stage 'runtime-notices'
New-Item -ItemType Directory -Path $notices | Out-Null
foreach($package in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
    $packageRoot=Join-Path $nuget "$package/$RuntimeVersion"
    $licenses=@(Get-ChildItem -LiteralPath $packageRoot -File | Where-Object Name -Match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$')
    if($licenses.Count -eq 0) { throw "缺少组件许可：$package" }
    foreach($file in $licenses) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $notices ($package+'-'+$file.Name)) }
}
$forbidden='(?i)(^|/)(AGENTS\.md|CLAUDE\.md|GEMINI\.md|profiles\.json|preferences\.json|baseline\.json|previous\.json|recovery-state\.json|request\.md|\.DS_Store|\._[^/]*|SYSTEM[^/]*\.md|PROMPT[^/]*\.md)$|(^|/)(logs?|transactions|\.venv|venv|__pycache__|__MACOSX|tmp|temp|cache|\.git)(/|$)|\.(pdb|log|out)$|prompt|提示词|需求'
$entries=@(Get-ChildItem -LiteralPath $stage -File -Recurse | ForEach-Object { [IO.Path]::GetRelativePath($stage,$_.FullName).Replace('\','/') })
if(@($entries | Where-Object { $_ -match $forbidden }).Count) { throw '候选目录包含禁止交付的文件。' }
$version=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $stage 'PingXu.dll')).ProductVersion
$manifest=[ordered]@{Version=$version;Status='candidate-not-hardware-approved';Runtime=$RuntimeVersion;SourceStamp=$stamp;Tests=$tests;Files=@(
    Get-ChildItem -LiteralPath $stage -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{Path=[IO.Path]::GetRelativePath($stage,$_.FullName).Replace('\','/');SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}
    })}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $stage 'build-manifest.json') -Encoding utf8
$destination=Join-Path $projectRoot ('dist/candidate-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $destination | Out-Null
$zip=Join-Path $destination 'PingXu.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage,$zip,[IO.Compression.CompressionLevel]::Optimal,$true)
$archive=[IO.Compression.ZipFile]::OpenRead($zip)
try { if(@($archive.Entries | Where-Object { $_.FullName -match $forbidden }).Count) { throw 'ZIP 内容复核失败，不得交付。' } }
finally { $archive.Dispose() }
$hash=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
"$hash  PingXu.zip" | Set-Content -LiteralPath ($zip+'.sha256') -Encoding ascii
Write-Output "候选包（未通过实机验收）：$zip"
Write-Output "SHA256：$hash"
[pscustomobject]@{PayloadPath=$stage;ZipPath=$zip;SourceStamp=$stamp}
