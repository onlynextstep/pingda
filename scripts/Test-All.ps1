$ErrorActionPreference='Stop'
$pingdaRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
foreach($name in @('PingXu.Update.Tests','PingXu.Settings.Tests','PingXu.Tests','PingXu.Log.Tests','PingXu.App.Tests','PingXu.Windows.Tests','PingXu.Studio.Tests','PingXu.Drag.Tests','PingXu.SceneManager.Tests')) {
    & dotnet run --project (Join-Path $pingdaRoot ('tests/'+$name)) -c Release
    if($LASTEXITCODE -ne 0){throw "测试失败：$name"}
}
