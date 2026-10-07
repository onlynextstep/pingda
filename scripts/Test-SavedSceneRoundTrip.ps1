param([int]$ProcessId, [string[]]$SceneNames=@('双屏专注','三屏协作'), [string]$EvidenceName='saved-scenes')
$ErrorActionPreference='Stop'
$testRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts/roundtrip-0.5'))
New-Item -ItemType Directory -Force $testRoot | Out-Null
$data=Join-Path $env:LOCALAPPDATA 'PingXu'
$exe=(Get-Process -Id $ProcessId).Path
if ([IO.Path]::GetFileName($exe) -ne 'PingXu.exe') { throw 'Not a PingXu process' }
$profiles=@(Get-Content (Join-Path $data 'profiles.json') -Raw | ConvertFrom-Json)
$markerPath=Join-Path $data 'recovery-state.json'
function Snapshot([string]$name) {
    $out=Join-Path $testRoot ($EvidenceName+'-'+$name+'.json')
    $diagnostic=Start-Process $exe -ArgumentList '--diagnose',('"'+$out+'"') -WindowStyle Hidden -Wait -PassThru
    if ($diagnostic.ExitCode -ne 0) { throw 'Capture failed' }
    return Get-Content $out -Raw | ConvertFrom-Json
}
function Assert-Profile($expected,$snapshot) {
    $want=@($expected.Displays | Where-Object Enabled); $actual=@($snapshot.Displays | Where-Object Enabled)
    if ($want.Count -ne $actual.Count) { throw "Enabled count: expected $($want.Count), actual $($actual.Count)" }
    foreach ($w in $want) {
        $a=@($actual | Where-Object Id -eq $w.Id)
        if ($a.Count -ne 1) { throw 'Missing stable display ID' }
        foreach ($field in @('Width','Height','Rotation','RefreshRate','X','Y','Primary')) {
            if ($w.$field -ne $a[0].$field) { throw "$($expected.Name): $field expected $($w.$field), actual $($a[0].$field)" }
        }
    }
}
$initial=Snapshot 'before'
for ($index=0; $index -lt $SceneNames.Count; $index++) {
    $scene=@($profiles | Where-Object Name -eq $SceneNames[$index])
    if ($scene.Count -ne 1) { throw 'Scene name must resolve uniquely' }; $scene=$scene[0]
    $old=Get-Content $markerPath -Raw | ConvertFrom-Json
    if ($old.Unresolved) { throw 'Unresolved transaction; no new operation allowed' }
    Write-Output "BEGIN $($scene.Name)"
    & "$PSScriptRoot/Test-DesktopUi.ps1" -ProcessId $ProcessId -Action Invoke -AutomationId ('Scene:'+$scene.Id)
    $deadline=[DateTime]::UtcNow.AddSeconds(90); $confirmed=$false; $done=$false
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 250
        $m=Get-Content $markerPath -Raw | ConvertFrom-Json
        if ($m.TransactionPath -ne $old.TransactionPath -and (Test-Path ($m.TransactionPath+'.status'))) {
            $s=Get-Content ($m.TransactionPath+'.status') -Raw | ConvertFrom-Json
            if ($s.Phase -eq 'trial' -and -not $confirmed) {
                $actual=Snapshot ($index.ToString()+'-trial'); Assert-Profile $scene $actual
                Write-Output "PASS trial readback $($scene.Name)"
                $controls=@(& "$PSScriptRoot/Test-DesktopUi.ps1" -ProcessId $ProcessId -Action List)
                if ($controls | Where-Object { $_.Id -eq 'RememberProfile' -and $_.Enabled -and -not $_.Offscreen }) {
                    & "$PSScriptRoot/Test-DesktopUi.ps1" -ProcessId $ProcessId -Action Toggle -AutomationId RememberProfile
                }
                & "$PSScriptRoot/Test-DesktopUi.ps1" -ProcessId $ProcessId -Action Invoke -AutomationId KeepDisplay
                $confirmed=$true
            }
            if ($s.Phase -in @('committed','reverted','error')) {
                $actual=Snapshot ($index.ToString()+'-after')
                if ($s.Phase -ne 'committed' -or -not $s.SafeToContinue) { throw "$($scene.Name): $($s.Phase): $($s.Message)" }
                Assert-Profile $scene $actual
                Write-Output "PASS committed $($scene.Name) / $($m.TransactionPath)"
                $done=$true; break
            }
        } else {
            $controls=@(& "$PSScriptRoot/Test-DesktopUi.ps1" -ProcessId $ProcessId -Action List)
            if ($controls | Where-Object { $_.Type -eq 'ControlType.Window' -and $_.Name -eq '未完成的编辑' }) {
                & "$PSScriptRoot/Test-DesktopUi.ps1" -ProcessId $ProcessId -Action Invoke -Name '是(Y)'
                continue
            }
            $errorWindow=$controls | Where-Object { $_.Type -eq 'ControlType.Window' -and $_.Name -in @('暂时无法切换','切换未完成','未能切换屏幕') }
            if ($errorWindow) {
                $status=($controls | Where-Object Id -eq StatusLabel).Name
                $null=Snapshot ($index.ToString()+'-rejected')
                $dismiss=if($errorWindow.Name -contains '未能切换屏幕'){'知道了'}else{'确定'}
                & "$PSScriptRoot/Test-DesktopUi.ps1" -ProcessId $ProcessId -Action Invoke -Name $dismiss
                throw "$($scene.Name) rejected before transaction: $status"
            }
        }
    }
    if (-not $done) { throw "No committed transition: $($scene.Name)" }
    Start-Sleep -Milliseconds 700
}
'PASS all requested saved-scene transitions, exact identity/mode/geometry readback'
