param(
    [string]$Source = (Join-Path $PSScriptRoot '../src/PingXu.App/Assets/pingxu-icon-source.png'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../src/PingXu.App/Assets')
)
# Format/size conversion only. Build-Brand.ps1 renders the approved SVG master first.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$inputPath = [IO.Path]::GetFullPath($Source)
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$sourceImage = [Drawing.Bitmap]::FromFile($inputPath)
try {
    if ($sourceImage.Width -ne $sourceImage.Height) { throw 'Icon source must be square.' }
    # Preserve intentional outer transparency and antialiased silhouette edges.
    if ($sourceImage.GetPixel(0,0).A -ne 0) { throw 'Icon outside silhouette must be transparent.' }
    $frames = @()
    foreach ($size in @(16,20,24,32,40,48,64,128,256)) {
        $bitmap = New-Object Drawing.Bitmap($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $buffer = New-Object IO.MemoryStream
        try {
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $attributes = New-Object Drawing.Imaging.ImageAttributes
            try {
                $attributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
                $graphics.DrawImage($sourceImage,[Drawing.Rectangle]::new(0,0,$size,$size),0,0,$sourceImage.Width,$sourceImage.Height,[Drawing.GraphicsUnit]::Pixel,$attributes)
            } finally { $attributes.Dispose() }
            # Resampling can spread a tiny alpha value into the extreme corners at 16px.
            foreach ($corner in @(@(0,0),@(($size-1),0),@(0,($size-1)),@(($size-1),($size-1)))) {
                $bitmap.SetPixel($corner[0],$corner[1],[Drawing.Color]::Transparent)
            }
            $bitmap.Save($buffer,[Drawing.Imaging.ImageFormat]::Png)
            $frames += [pscustomobject]@{Size=$size;Bytes=$buffer.ToArray()}
            if ($size -eq 256) { $bitmap.Save((Join-Path $outputPath 'pingxu-icon.png'),[Drawing.Imaging.ImageFormat]::Png) }
        } finally { $buffer.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $file = [IO.File]::Create((Join-Path $outputPath 'PingXu.ico'))
    $writer = New-Object IO.BinaryWriter($file)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $encodedSize = if($frame.Size -eq 256){0}else{$frame.Size}
            $writer.Write([byte]$encodedSize); $writer.Write([byte]$encodedSize)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    } finally { $writer.Dispose(); $file.Dispose() }
    Write-Output "Icon ready: $outputPath (9 PNG frames, 16 to 256 px; outer transparency preserved)"
} finally { $sourceImage.Dispose() }
