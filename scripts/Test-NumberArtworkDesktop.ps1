param([Parameter(Mandatory=$true)][int]$ProcessId,
    [Parameter(Mandatory=$true)][string]$SnapshotPath,
    [Parameter(Mandatory=$true)][string]$OutputDirectory)
# Scoped UI-only smoke test: shows identification overlays, never changes display settings or presets.
$ErrorActionPreference = 'Stop'
if ((Get-Process -Id $ProcessId).ProcessName -ne 'PingXu') { throw 'Only PingXu is allowed.' }
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing,System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NumberDesktopProbe {
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
'@
[void][NumberDesktopProbe]::SetThreadDpiAwarenessContext([IntPtr](-4))
$snapshot = Get-Content -LiteralPath $SnapshotPath -Raw -Encoding UTF8 | ConvertFrom-Json
$connected = @($snapshot.Displays | Where-Object Connected | Sort-Object Id)
$active = @($connected | Where-Object Enabled)
$processCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$ProcessId)
$roots = [System.Windows.Automation.AutomationElement]::RootElement
$windows = $roots.FindAll([System.Windows.Automation.TreeScope]::Children,$processCondition)
$main = @($windows | Where-Object { $_.Current.Name -eq '屏序 · 多屏工作空间' })
if ($main.Count -ne 1) { throw 'Expected exactly one PingXu main window.' }
$all = [System.Windows.Automation.Condition]::TrueCondition
$images = @($main[0].FindAll([System.Windows.Automation.TreeScope]::Descendants,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Image -and $_.Current.AutomationId -like 'Studio.Number:*' })
if ($images.Count -ne $active.Count) { throw 'Visible canvas images do not match active screens.' }
foreach ($image in $images) {
    if ($image.Current.IsOffscreen -or $image.Current.BoundingRectangle.Width -le 0) { throw 'Canvas number image is hidden.' }
}
$identifyCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'识别屏幕')
$identify = $main[0].FindFirst([System.Windows.Automation.TreeScope]::Descendants,$identifyCondition)
if (-not $identify -or -not $identify.Current.IsEnabled) { throw 'Identify action is unavailable.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$identify.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
$timer = [Diagnostics.Stopwatch]::StartNew()
do {
    $overlays = @($roots.FindAll([System.Windows.Automation.TreeScope]::Children,$processCondition) | Where-Object { $_.Current.Name -eq '屏幕识别' })
    if ($overlays.Count -eq $active.Count) { break }
    Start-Sleep -Milliseconds 40
} while ($timer.ElapsedMilliseconds -lt 1800)
if ($overlays.Count -ne $active.Count) { throw "Expected $($active.Count) identification windows; got $($overlays.Count)." }
$records = @()
foreach ($overlay in $overlays) {
    $elements = @($overlay.FindAll([System.Windows.Automation.TreeScope]::Descendants,$all))
    $numberImage = @($elements | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Image })
    if ($numberImage.Count -ne 1 -or $numberImage[0].Current.Name -notmatch '编号 (\d+)$') { throw 'Overlay must have one accessible image number.' }
    $number = [int]$Matches[1]
    if ($number -lt 1 -or $number -gt $connected.Count) { throw 'Invalid overlay number.' }
    $expected = $connected[$number - 1]
    $screen = @([Windows.Forms.Screen]::AllScreens | Where-Object { $_.DeviceName -eq $expected.DeviceName })
    if ($screen.Count -ne 1) { throw 'Snapshot device is absent from the active desktop.' }
    $rect = $overlay.Current.BoundingRectangle
    $center = New-Object Drawing.Point([int]($rect.X + $rect.Width / 2),[int]($rect.Y + $rect.Height / 2))
    if (-not $screen[0].Bounds.Contains($center)) { throw "Number $number is displayed on the wrong monitor." }
    if ($rect.Width -le 0 -or $rect.Height -le 0 -or $numberImage[0].Current.IsOffscreen) { throw 'Overlay image is not visible.' }
    $bitmap = New-Object Drawing.Bitmap([int]$rect.Width,[int]$rect.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $path = Join-Path $OutputDirectory ('identify-{0:00}.png' -f $number)
    try { $graphics.CopyFromScreen([int]$rect.X,[int]$rect.Y,0,0,$bitmap.Size); $bitmap.Save($path) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
    $records += [PSCustomObject]@{ Number=$number; Device=$expected.DeviceName; Rectangle=$rect.ToString(); Image=$path }
}
if (@($records.Number | Select-Object -Unique).Count -ne $active.Count) { throw 'Duplicate screen numbers.' }
# Let the application's three-second overlay timer finish; do not close unrelated windows.
do {
    $remaining = @($roots.FindAll([System.Windows.Automation.TreeScope]::Children,$processCondition) | Where-Object { $_.Current.Name -eq '屏幕识别' })
    if ($remaining.Count -eq 0) { break }
    Start-Sleep -Milliseconds 60
} while ($timer.ElapsedMilliseconds -lt 5500)
if ($remaining.Count -ne 0) { throw 'Identification overlay did not dismiss automatically.' }
$report = [PSCustomObject]@{ Passed=$true; CanvasImageCount=$images.Count; IdentificationWindows=$records; AutoDismissed=$true; DisplaySettingsChanged=$false }
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'desktop-number-report.json'),($report | ConvertTo-Json -Depth 5))
$report | ConvertTo-Json -Depth 5
