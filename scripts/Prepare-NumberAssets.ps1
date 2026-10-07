[CmdletBinding()]
param([string]$SourceDirectory)
$ErrorActionPreference = 'Stop'
if (-not $SourceDirectory) { $SourceDirectory = Join-Path $PSScriptRoot '../../../pic' }
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'NumberAssetExtractor.cs'))) -ReferencedAssemblies System.Drawing
$assetDirectory = Join-Path $PSScriptRoot '../src/PingXu.App/Assets/Numbers'
$archiveDirectory = Join-Path $PSScriptRoot '../design/number-sources'
New-Item -ItemType Directory -Force -Path $assetDirectory, $archiveDirectory | Out-Null
$times = @('14_53_56', '14_54_02', '14_54_07', '14_54_12', '14_54_16', '14_54_20')
for ($index = 0; $index -lt $times.Count; $index++) {
    $number = '{0:00}' -f ($index + 1)
    $candidates = @(Get-ChildItem -LiteralPath $SourceDirectory -Filter "* $($times[$index]).png" -File)
    if ($candidates.Count -ne 1) { throw "Expected exactly one source image for $number" }
    $inputPath = $candidates[0].FullName
    $archive = Join-Path $archiveDirectory "$number.png"
    if (Test-Path -LiteralPath $archive) {
        if ((Get-FileHash -LiteralPath $archive).Hash -ne (Get-FileHash -LiteralPath $inputPath).Hash) {
            throw "Archived source $number differs from input; refusing overwrite."
        }
    } else { Copy-Item -LiteralPath $inputPath -Destination $archive }
    $output = [IO.Path]::GetFullPath((Join-Path $assetDirectory "$number.png"))
    $crop = [NumberAssetExtractor]::Extract($inputPath, $output)
    [PSCustomObject]@{ Number = $number; Crop = ($crop -join ','); SourceSHA256 = (Get-FileHash -LiteralPath $inputPath).Hash; Output = $output }
}
