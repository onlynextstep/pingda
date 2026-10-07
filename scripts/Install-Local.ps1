param([string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs\PingXu'))
$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\dist\PingXu'))
$target = [IO.Path]::GetFullPath($Destination)
if (-not (Test-Path -LiteralPath (Join-Path $source 'PingXu.exe'))) { throw '请先构建 dist\PingXu。' }
if (Test-Path -LiteralPath (Join-Path $target 'PingXu.exe')) { throw '此位置已有屏搭。请关闭旧版并先备份程序目录，或选择新的 Destination。用户预设无需移动。' }
New-Item -ItemType Directory -Force -Path $target | Out-Null
Get-ChildItem -LiteralPath $source -File | Copy-Item -Destination $target
$desktopPath = [Environment]::GetFolderPath('DesktopDirectory')
$shortcutPath = Join-Path $desktopPath '屏搭.lnk'
if (Test-Path -LiteralPath $shortcutPath) { throw "程序已复制，但桌面已有同名快捷方式，未覆盖：$shortcutPath" }
$shellObject = New-Object -ComObject WScript.Shell
$shortcut = $shellObject.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $target 'PingXu.exe'
$shortcut.WorkingDirectory = $target
$shortcut.IconLocation = (Join-Path $target 'PingXu.exe') + ',0'
$shortcut.Description = '屏搭 · 中文多屏工作空间'
$shortcut.Save()
Write-Output "程序：$(Join-Path $target 'PingXu.exe')"
Write-Output "桌面快捷方式：$shortcutPath"
