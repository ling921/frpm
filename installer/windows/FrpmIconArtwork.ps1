# Shared vector artwork. Render at 4x resolution before downsampling to preserve symmetry and alpha.
Add-Type -AssemblyName System.Drawing

function New-FrpmPngFrame {
    param([Parameter(Mandatory = $true)][int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new(
        ($Size * 4), ($Size * 4), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.ScaleTransform(4, 4)

    $background = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(84, 104, 255))
    $node = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(191, 199, 255))
    $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $stroke = [single]($Size * 0.078)
    $line = [System.Drawing.Pen]::new([System.Drawing.Color]::White, $stroke)
    $line.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $line.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()

    try {
        $radius = [single]($Size * 0.22)
        $diameter = $radius * 2
        $path.AddArc(0, 0, $diameter, $diameter, 180, 90)
        $path.AddArc($Size - $diameter, 0, $diameter, $diameter, 270, 90)
        $path.AddArc($Size - $diameter, $Size - $diameter, $diameter, $diameter, 0, 90)
        $path.AddArc(0, $Size - $diameter, $diameter, $diameter, 90, 90)
        $path.CloseFigure()
        $graphics.FillPath($background, $path)

        $start = [single]($Size * 0.31)
        $end = [single]($Size * 0.69)
        $nodeSize = [single]($Size * 0.16)
        $halfNode = [single]($nodeSize / 2)
        $centerSize = [single]($Size * 0.19)
        $centerOffset = [single](($Size - $centerSize) / 2)

        $graphics.DrawLine($line, $start, $start, $end, $end)
        $graphics.DrawLine($line, $end, $start, $start, $end)
        foreach ($point in @(@($start, $start), @($end, $start), @($start, $end), @($end, $end))) {
            $graphics.FillEllipse($node, $point[0] - $halfNode, $point[1] - $halfNode, $nodeSize, $nodeSize)
        }
        $graphics.FillEllipse($white, $centerOffset, $centerOffset, $centerSize, $centerSize)

        $stream = [System.IO.MemoryStream]::new()
        try {
            $scaled = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $resize = [System.Drawing.Graphics]::FromImage($scaled)
            try {
                $resize.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $resize.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $resize.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $resize.DrawImage($bitmap, 0, 0, $Size, $Size)
                $scaled.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            }
            finally { $resize.Dispose(); $scaled.Dispose() }
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

