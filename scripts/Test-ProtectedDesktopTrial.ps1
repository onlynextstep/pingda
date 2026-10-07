param([int]$ProcessId, [string]$EvidenceName, [ValidateSet('Timeout','Cancel','Keep')][string]$Decision='Timeout', [ValidatePattern('^(ApplyButton|RestoreButton|Scene:[a-zA-Z0-9_-]+)$')][string]$ApplyAutomationId='ApplyButton', [switch]$DontAskAgain)
# Drives the real app's normal Apply/guardian flow; never calls native display writes itself.
$ErrorActionPreference='Stop'
$data=Join-Path $env:LOCALAPPDATA 'PingXu'
$artifact=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts'))
$marker=Join-Path $data 'recovery-state.json'
$old=Get-Content $marker -Raw | ConvertFrom-Json
if($old.Unresolved){throw 'Existing unresolved transaction; refusing a new trial.'}
$beforePath=Join-Path $artifact ($EvidenceName+'-before.json')
Start-Process -FilePath (Get-Process -Id $ProcessId).Path -ArgumentList '--diagnose',('"'+$beforePath+'"') -WindowStyle Hidden -Wait
& "$PSScriptRoot/Test-DesktopUi.ps1" -ProcessId $ProcessId -Action Invoke -AutomationId $ApplyAutomationId
$deadline=[DateTime]::UtcNow.AddSeconds(100)
$captured=$false
$newPath=$null
while([DateTime]::UtcNow -lt $deadline){
    Start-Sleep -Milliseconds 200
    $m=Get-Content $marker -Raw | ConvertFrom-Json
    if($m.TransactionPath -eq $old.TransactionPath){continue}
    $newPath=$m.TransactionPath
    $statusPath=$newPath+'.status'
    if(-not(Test-Path $statusPath)){continue}
    $s=Get-Content $statusPath -Raw | ConvertFrom-Json
    if($s.Phase -eq 'trial' -and -not $captured){
        $out=Join-Path $artifact ($EvidenceName+'-trial.json')
        Start-Process -FilePath (Get-Process -Id $ProcessId).Path -ArgumentList '--diagnose',('"'+$out+'"') -WindowStyle Hidden -Wait
        $snapshot=Get-Content $out -Raw | ConvertFrom-Json
        $request=Get-Content $newPath -Raw | ConvertFrom-Json
        $expected=@($request.Profile.Displays | Where-Object Enabled)
        $actual=@($snapshot.Displays | Where-Object Enabled)
        if($expected.Count -ne $actual.Count){throw 'Active output count differs from requested profile.'}
        foreach($e in $expected){
            $a=@($actual | Where-Object Id -eq $e.Id)
            if($a.Count -ne 1){throw 'Stable output identity missing or duplicated.'}
            foreach($field in @('Width','Height','Rotation','RefreshRate','X','Y','Primary')){
                if($a[0].$field -ne $e.$field){throw "Native readback differs: $field"}
            }
        }
        Write-Output 'PASS: every enabled output matches requested identity, size, rotation, refresh, position and primary.'
        $snapshot.Displays | Select-Object DeviceName,Width,Height,Rotation,RefreshRate,X,Y,Primary,Enabled | Format-Table
        Write-Output "TRIAL captured: $newPath"
        $captured=$true
        if($DontAskAgain){
            Start-Sleep -Milliseconds 500
            & "$PSScriptRoot/Test-DesktopUi.ps1" -ProcessId $ProcessId -Action Toggle -AutomationId DontAskAgain
        }
        if($Decision -ne 'Timeout'){
            $button=if($Decision -eq 'Keep'){'保留此布局'}else{'恢复原布局'}
            Start-Sleep -Milliseconds 500
            & "$PSScriptRoot/Test-DesktopUi.ps1" -ProcessId $ProcessId -Action Invoke -Name $button
        }
    }
    if($s.Phase -in @('committed','reverted','error')){
        $s | Format-List
        if(-not $captured){throw "No trial phase observed: $newPath"}
        if($s.Phase -eq 'error'){throw 'Guardian reported failure'}
        $out=Join-Path $artifact ($EvidenceName+'-after.json')
        Start-Process -FilePath (Get-Process -Id $ProcessId).Path -ArgumentList '--diagnose',('"'+$out+'"') -WindowStyle Hidden -Wait
        if($Decision -ne 'Keep'){
            # Windows may renumber source routes when outputs are re-enabled. Report raw differences,
            # but require exact per-device desktop/source/signal contracts after stable-ID rebinding.
            & dotnet run --project "$PSScriptRoot/../tests/PingXu.Windows.Tests" -c Release -- --compare-snapshots $beforePath $out
            if($LASTEXITCODE -ne 0){throw 'Restored snapshot differs from the original desktop/native contract.'}
        } elseif($s.Phase -ne 'committed'){throw 'Keep action did not persist layout.'}
        Write-Output "Completed protected UI test: $EvidenceName / $Decision / $newPath"
        return
    }
}
throw "No completed new transaction within 100s: $newPath"
