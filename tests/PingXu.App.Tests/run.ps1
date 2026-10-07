$ErrorActionPreference = 'Stop'
$testRoot = $PSScriptRoot
$outputRoot = Join-Path $testRoot 'artifacts'
$buildRoot = Join-Path $outputRoot 'build'
$projectFile = Join-Path $testRoot 'PingXu.App.Tests.csproj'
$savedEnvironment = @{}
$isolatedEnvironment = @{
    DOTNET_CLI_HOME = (Join-Path $outputRoot 'dotnet-home')
    NUGET_PACKAGES = (Join-Path $outputRoot 'nuget-packages')
    NUGET_HTTP_CACHE_PATH = (Join-Path $outputRoot 'nuget-http-cache')
    NUGET_PLUGINS_CACHE_PATH = (Join-Path $outputRoot 'nuget-plugins-cache')
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
    DOTNET_NOLOGO = '1'
}
try {
    foreach ($entry in $isolatedEnvironment.GetEnumerator()) {
        $savedEnvironment[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, 'Process')
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
    New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
    # ArtifactsPath applies to every project reference: no src/bin or src/obj writes.
    & dotnet build $projectFile --artifacts-path $buildRoot --configuration Debug --nologo -p:NuGetAudit=false 2>&1 |
        Tee-Object -FilePath (Join-Path $outputRoot 'build-output.txt')
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE (see artifacts/build-output.txt)" }
    $binary = Join-Path $buildRoot 'bin/PingXu.App.Tests/debug/PingXu.App.Tests.exe'
    & $binary 2>&1 | Tee-Object -FilePath (Join-Path $outputRoot 'test-output.txt')
    if ($LASTEXITCODE -ne 0) { throw "UI smoke failed: $LASTEXITCODE (see artifacts/run-report.md)" }
}
finally {
    foreach ($entry in $savedEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
}
