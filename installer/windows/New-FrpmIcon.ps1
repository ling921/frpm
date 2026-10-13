param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-FrpmPngFrame {
    param([Parameter(Mandatory = $true)][int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $background = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(84, 104, 255))
    $node = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(191, 199, 255))
    $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $stroke = [Math]::Max(1, [int][Math]::Round($Size * 0.078))
    $line = [System.Drawing.Pen]::new([System.Drawing.Color]::White, $stroke)
    $line.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $line.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()

    try {
        $radius = [Math]::Max(2, [int][Math]::Round($Size * 0.22))
        $diameter = $radius * 2
        $path.AddArc(0, 0, $diameter, $diameter, 180, 90)
        $path.AddArc($Size - $diameter, 0, $diameter, $diameter, 270, 90)
        $path.AddArc($Size - $diameter, $Size - $diameter, $diameter, $diameter, 0, 90)
        $path.AddArc(0, $Size - $diameter, $diameter, $diameter, 90, 90)
        $path.CloseFigure()
        $graphics.FillPath($background, $path)

        $start = [int][Math]::Round($Size * 0.31)
        $end = [int][Math]::Round($Size * 0.69)
        $nodeSize = [Math]::Max(2, [int][Math]::Round($Size * 0.16))
        $halfNode = [int][Math]::Floor($nodeSize / 2)
        $centerSize = [Math]::Max(2, [int][Math]::Round($Size * 0.19))
        $centerOffset = [int][Math]::Floor(($Size - $centerSize) / 2)

        $graphics.DrawLine($line, $start, $start, $end, $end)
        $graphics.DrawLine($line, $end, $start, $start, $end)
        foreach ($point in @(@($start, $start), @($end, $start), @($start, $end), @($end, $end))) {
            $graphics.FillEllipse($node, $point[0] - $halfNode, $point[1] - $halfNode, $nodeSize, $nodeSize)
        }
        $graphics.FillEllipse($white, $centerOffset, $centerOffset, $centerSize, $centerSize)

        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return ,$stream.ToArray()
        }
        finally {
            $stream.Dispose()
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
}

$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [System.IO.Path]::GetDirectoryName($OutputPath)
if ($outputDirectory) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

# PNG-backed ICO frames preserve the artwork's RGBA transparency and exact colors.
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = [System.Collections.Generic.List[object]]::new()
foreach ($size in $sizes) {
    $frames.Add([pscustomobject]@{ Size = $size; Bytes = (New-FrpmPngFrame -Size $size) })
}

$stream = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)

    $offset = 6 + (16 * $frames.Count)
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { [byte]0 } else { [byte]$frame.Size }
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }

    foreach ($frame in $frames) {
        $writer.Write([byte[]]$frame.Bytes)
    }
    $writer.Flush()
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

Write-Output "Created transparent multi-size icon: $OutputPath"
