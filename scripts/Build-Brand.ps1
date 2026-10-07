param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../src/PingXu.App/Assets'))
# The approved square/chamfer vector master is the sole geometry source for every exported size.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
[xml]$svg = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../src/PingXu.App/Assets/pingxu-logo.svg') -Raw
$bitmap = [Drawing.Bitmap]::new(1024,1024,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.Clear([Drawing.Color]::Transparent)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform(2,2)
    foreach ($rect in $svg.svg.ChildNodes) {
        if ($rect.LocalName -in @('title','defs')) { continue }
        if ($rect.LocalName -eq 'polygon') {
            $points = [Drawing.PointF[]]@($rect.points.Split(' ',[StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object {
                $pair = $_.Split(','); [Drawing.PointF]::new([single]$pair[0],[single]$pair[1])
            })
            $cutout = $rect.fill -eq 'none'
            $color = if ($cutout) { [Drawing.Color]::Transparent } else { [Drawing.ColorTranslator]::FromHtml($rect.fill) }
            $brush = [Drawing.SolidBrush]::new($color)
            if ($cutout) { $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy }
            try { $graphics.FillPolygon($brush,$points) } finally { $brush.Dispose(); $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceOver }
            continue
        }
        if ($rect.LocalName -ne 'rect') { throw "Unsupported SVG element: $($rect.LocalName)" }
        $x=[single]$rect.x; $y=[single]$rect.y; $w=[single]$rect.width; $h=[single]$rect.height
        $radius = if ($rect.HasAttribute('rx')) { [single]$rect.rx } else { 0 }
        $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($rect.fill))
        $path = [Drawing.Drawing2D.GraphicsPath]::new()
        try {
            if ($radius -gt 0) {
                $d = 2*$radius
                $path.AddArc($x,$y,$d,$d,180,90)
                $path.AddArc(($x+$w-$d),$y,$d,$d,270,90)
                $path.AddArc(($x+$w-$d),($y+$h-$d),$d,$d,0,90)
                $path.AddArc($x,($y+$h-$d),$d,$d,90,90)
                $path.CloseFigure()
                $graphics.FillPath($brush,$path)
            } else { $graphics.FillRectangle($brush,$x,$y,$w,$h) }
        } finally { $path.Dispose(); $brush.Dispose() }
    }
    $source = Join-Path $outputPath 'pingxu-icon-source.png'
    $bitmap.Save($source,[Drawing.Imaging.ImageFormat]::Png)
} finally { $graphics.Dispose(); $bitmap.Dispose() }
& (Join-Path $PSScriptRoot 'Build-Icon.ps1') -Source $source -OutputDirectory $outputPath
