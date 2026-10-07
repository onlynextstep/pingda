param(
    [ValidateSet('List','Invoke','Select','Expand','Collapse','Toggle','Capture','Close','SetValue','RestoreWindow','Drag')][string]$Action = 'List',
    [int]$ProcessId,
    [string]$Name,
    [string]$AutomationId,
    [string]$OutputPath,
    [string]$WindowName,
    [string]$Value,
    [int]$DeltaX,
    [int]$DeltaY,
    [switch]$CancelWithEscape
)
# Explicitly scoped to the specified PingXu process. Never sends input to unrelated applications.
$ErrorActionPreference = 'Stop'
$appProcess = Get-Process -Id $ProcessId
if ($appProcess.ProcessName -ne 'PingXu') { throw 'Target must be a PingXu process.' }
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Windows.Forms,System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PingXuUiProbe {
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
 [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
}
'@
[void][PingXuUiProbe]::SetProcessDPIAware()
[void][PingXuUiProbe]::SetThreadDpiAwarenessContext([IntPtr](-4))
$processCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$ProcessId)
$windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$processCondition)
if ($windows.Count -eq 0) { throw 'No visible PingXu UI Automation window.' }
if ($Action -eq 'List') {
    foreach ($window in $windows) {
        $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object {
            try {
                if ($null -eq $_) { return }
                $c=$_.Current
                if ($null -eq $c.ControlType -or $null -eq $c.BoundingRectangle) { return }
                if ($c.ControlType.ProgrammaticName -notin @('ControlType.Text','ControlType.Pane') -or $c.AutomationId -eq 'StatusLabel') {
                    [PSCustomObject]@{ Window=$window.Current.Name; Type=$c.ControlType.ProgrammaticName; Name=$c.Name; Id=$c.AutomationId; Enabled=$c.IsEnabled; Offscreen=$c.IsOffscreen; Rect=$c.BoundingRectangle.ToString() }
                }
            } catch [System.Windows.Automation.ElementNotAvailableException] { }
        }
    }
    return
}
$window = $windows | Where-Object { $_.Current.Name -like '屏序*' } | Select-Object -First 1
if ($WindowName) {
    $window = $windows | Where-Object { $_.Current.Name -eq $WindowName } | Select-Object -First 1
    if (-not $window) {
        $nameCondition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,$WindowName)
        $window=@(foreach($w in $windows){$w.FindAll([System.Windows.Automation.TreeScope]::Descendants,$nameCondition)}) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window } | Select-Object -First 1
    }
    if (-not $window) { throw "Specified window not found: $WindowName" }
}
if (-not $window) { $window=$windows[0] }
if ($Action -eq 'RestoreWindow') { $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal); return }
if ($Action -eq 'Capture') {
    if (-not $OutputPath) { throw 'OutputPath required' }
    $window.SetFocus()
    [void][PingXuUiProbe]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)
    Start-Sleep -Milliseconds 300
    if ([PingXuUiProbe]::GetForegroundWindow() -ne [IntPtr]$window.Current.NativeWindowHandle) { throw 'PingXu is not foreground; refusing to capture unrelated windows.' }
    $r=$window.Current.BoundingRectangle
    $bitmap=New-Object System.Drawing.Bitmap([int]$r.Width,[int]$r.Height)
    $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.CopyFromScreen([int]$r.X,[int]$r.Y,0,0,$bitmap.Size); $bitmap.Save($OutputPath) } finally { $graphics.Dispose();$bitmap.Dispose() }
    Write-Output $OutputPath
    return
}
if ($Action -eq 'Close') { $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close(); return }
if (-not $Name -and -not $AutomationId) { throw 'Exact Name or AutomationId required' }
$property=if($AutomationId){[System.Windows.Automation.AutomationElement]::AutomationIdProperty}else{[System.Windows.Automation.AutomationElement]::NameProperty}
$targetValue=if($AutomationId){$AutomationId}else{$Name}
$condition=New-Object System.Windows.Automation.PropertyCondition($property,$targetValue)
$found=@(foreach($w in $windows){$w.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition) | ForEach-Object {$_}})
if ($Action -eq 'Invoke') { $found=@($found | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button }) }
if ($Action -eq 'Select') { $found=@($found | Where-Object { $_.Current.ControlType.ProgrammaticName -in @('ControlType.RadioButton','ControlType.ListItem') }) }
$found=@($found | Group-Object -Property { $_.GetRuntimeId() -join '.' } | ForEach-Object { $_.Group[0] })
if ($found.Count -ne 1) { throw "Expected one exact UI target, found $($found.Count): $targetValue" }
$target=$found[0]
if (-not $target.Current.IsEnabled) { throw 'Control disabled; no input sent.' }
if ($Action -eq 'Drag') {
    $window.SetFocus()
    [void][PingXuUiProbe]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)
    Start-Sleep -Milliseconds 300
    if ([PingXuUiProbe]::GetForegroundWindow() -ne [IntPtr]$window.Current.NativeWindowHandle) { throw 'PingXu must be foreground before drag.' }
    $rect = $target.Current.BoundingRectangle
    if ($target.Current.IsOffscreen -or $rect.IsEmpty -or $rect.Width -le 0) { throw 'Drag target is not visible.' }
    $sx = [int]($rect.X + $rect.Width / 2); $sy = [int]($rect.Y + $rect.Height / 2)
    $endPoint = New-Object System.Windows.Point(($sx + $DeltaX),($sy + $DeltaY))
    if (-not $window.Current.BoundingRectangle.Contains($endPoint)) { throw 'Drag endpoint must stay inside PingXu.' }
    [void][PingXuUiProbe]::SetCursorPos($sx,$sy)
    [PingXuUiProbe]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    try {
        for ($step=1; $step -le 24; $step++) {
            if ([PingXuUiProbe]::GetForegroundWindow() -ne [IntPtr]$window.Current.NativeWindowHandle) { throw 'Foreground changed during drag.' }
            [void][PingXuUiProbe]::SetCursorPos(($sx + [int]($DeltaX*$step/24)),($sy + [int]($DeltaY*$step/24)))
            Start-Sleep -Milliseconds 20
        }
        if ($CancelWithEscape) {
            if ([PingXuUiProbe]::GetForegroundWindow() -ne [IntPtr]$window.Current.NativeWindowHandle) { throw 'Foreground changed before Escape.' }
            [PingXuUiProbe]::keybd_event(0x1B,0,0,[UIntPtr]::Zero)
            [PingXuUiProbe]::keybd_event(0x1B,0,2,[UIntPtr]::Zero)
            Start-Sleep -Milliseconds 80
        }
    } finally { [PingXuUiProbe]::mouse_event(4,0,0,0,[UIntPtr]::Zero) }
    Write-Output "Drag completed: $targetValue delta=($DeltaX,$DeltaY)"
    return
}
switch($Action){
 'SetValue' {$target.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value)}
 'Invoke' {$target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()}
 'Select' {$target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()}
 'Expand' {$target.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()}
 'Collapse' {$target.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()}
 'Toggle' {$target.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()}
}
Write-Output "$Action completed: $targetValue"
