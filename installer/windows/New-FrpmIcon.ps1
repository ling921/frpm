param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

Add-Type -AssemblyName System.Drawing

$size = 256
$bitmap = [System.Drawing.Bitmap]::new($size, $size)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)

$background = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(84, 104, 255))
$node = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(191, 199, 255))
$white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
$line = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 20)
$line.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$line.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$path = [System.Drawing.Drawing2D.GraphicsPath]::new()

try {
    $radius = 56
    $diameter = $radius * 2
    $path.AddArc(0, 0, $diameter, $diameter, 180, 90)
    $path.AddArc($size - $diameter, 0, $diameter, $diameter, 270, 90)
    $path.AddArc($size - $diameter, $size - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc(0, $size - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    $graphics.FillPath($background, $path)

    $graphics.DrawLine($line, 80, 80, 176, 176)
    $graphics.DrawLine($line, 176, 80, 80, 176)
    foreach ($point in @(@(80, 80), @(176, 80), @(80, 176), @(176, 176))) {
        $graphics.FillEllipse($node, $point[0] - 20, $point[1] - 20, 40, 40)
    }
    $graphics.FillEllipse($white, 104, 104, 48, 48)

    $directory = Split-Path -Parent $OutputPath
    if ($directory) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }
    $icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
    try {
        $stream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create)
        try {
            $icon.Save($stream)
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $icon.Dispose()
    }
}
finally {
    $path.Dispose()
    $line.Dispose()
    $white.Dispose()
    $node.Dispose()
    $background.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}
