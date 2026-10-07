param([int]$ProcessId, [string]$EvidenceName, [int]$ObserveSeconds = 23)
# Invoke the installed UI. Assert this NEW transaction has explicitly disabled confirmation,
# reaches committed with no trial phase, and remains unchanged past the old 20-second deadline.
$ErrorActionPreference='Stop'
$artifact=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../artifacts'))
$marker=Join-Path $env:LOCALAPPDATA 'PingXu/recovery-state.json'
$old=Get-Content $marker -Raw | ConvertFrom-Json
if($old.Unresolved){throw 'Unresolved transaction'}
$exe=(Get-Process -Id $ProcessId).Path
& "$PSScriptRoot/Test-DesktopUi.ps1" -ProcessId $ProcessId -Action Invoke -AutomationId ApplyButton
$deadline=[DateTime]::UtcNow.AddSeconds(45)
$completed=$false
while([DateTime]::UtcNow -lt $deadline){
    Start-Sleep -Milliseconds 150
    $m=Get-Content $marker -Raw|ConvertFrom-Json
    if($m.TransactionPath -eq $old.TransactionPath){continue}
    $request=Get-Content $m.TransactionPath -Raw|ConvertFrom-Json
    if($request.RequiresConfirmation -ne $false){throw 'Unexpected manual-confirmation request'}
    if(-not(Test-Path ($m.TransactionPath+'.status'))){continue}
    $status=Get-Content ($m.TransactionPath+'.status') -Raw|ConvertFrom-Json
    if($status.Phase -eq 'trial'){throw 'Direct switch entered a confirmation trial'}
    if($status.Phase -in @('reverted','error')){throw ('Direct switch failed: '+$status.Message)}
    if($status.Phase -eq 'committed'){
        if(-not $status.SafeToContinue){throw 'Commit did not release recovery protection'}
        $completed=$true; break
    }
}
if(-not $completed){throw 'No new direct commit within 45 seconds'}
Write-Output "COMMITTED without confirmation: $($m.TransactionPath)"
$first=Join-Path $artifact ($EvidenceName+'-committed.json')
Start-Process $exe -ArgumentList '--diagnose',('"'+$first+'"') -WindowStyle Hidden -Wait
$snapshot=Get-Content $first -Raw|ConvertFrom-Json
$actual=@($snapshot.Displays|Where-Object Enabled)
$expected=@($request.Profile.Displays|Where-Object Enabled)
if($actual.Count -ne $expected.Count){throw 'Wrong active output count'}
foreach($e in $expected){
    $a=@($actual|Where-Object Id -eq $e.Id)
    if($a.Count -ne 1){throw 'Output identity mismatch'}
    foreach($field in @('Width','Height','Rotation','X','Y','Primary','RefreshRate')){
        if($a[0].$field -ne $e.$field){throw "Readback mismatch: $field"}
    }
}
$actual|Select-Object DeviceName,Width,Height,Rotation,X,Y,Primary,RefreshRate|Format-Table
Start-Sleep -Seconds $ObserveSeconds
$after=Join-Path $artifact ($EvidenceName+'-observed.json')
Start-Process $exe -ArgumentList '--diagnose',('"'+$after+'"') -WindowStyle Hidden -Wait
& dotnet run --project "$PSScriptRoot/../tests/PingXu.Windows.Tests" -c Release -- --compare-snapshots $first $after
if($LASTEXITCODE -ne 0){throw 'Layout changed during observation'}
Write-Output "PASS: request/readback matched; no timeout reversal after $ObserveSeconds seconds."
