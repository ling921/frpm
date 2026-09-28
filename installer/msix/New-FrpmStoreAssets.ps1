param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

Add-Type -AssemblyName System.Drawing

function New-FrpmLogo {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Size,
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $background = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(84, 104, 255))
    $node = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(191, 199, 255))
    $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $line = [System.Drawing.Pen]::new([System.Drawing.Color]::White, [Math]::Max(2, [int]($Size * 0.078)))
    $line.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $line.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()

    try {
        $radius = [int]($Size * 0.22)
        $diameter = $radius * 2
        $path.AddArc(0, 0, $diameter, $diameter, 180, 90)
        $path.AddArc($Size - $diameter, 0, $diameter, $diameter, 270, 90)
        $path.AddArc($Size - $diameter, $Size - $diameter, $diameter, $diameter, 0, 90)
        $path.AddArc(0, $Size - $diameter, $diameter, $diameter, 90, 90)
        $path.CloseFigure()
        $graphics.FillPath($background, $path)

        $start = [int]($Size * 0.31)
        $end = [int]($Size * 0.69)
        $nodeSize = [int]($Size * 0.16)
        $halfNode = [int]($nodeSize / 2)
        $centerSize = [int]($Size * 0.19)
        $centerOffset = [int](($Size - $centerSize) / 2)

        $graphics.DrawLine($line, $start, $start, $end, $end)
        $graphics.DrawLine($line, $end, $start, $start, $end)
        foreach ($point in @(@($start, $start), @($end, $start), @($start, $end), @($end, $end))) {
            $graphics.FillEllipse($node, $point[0] - $halfNode, $point[1] - $halfNode, $nodeSize, $nodeSize)
        }
        $graphics.FillEllipse($white, $centerOffset, $centerOffset, $centerSize, $centerSize)
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
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

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
New-FrpmLogo -Size 256 -Path (Join-Path $OutputDirectory 'StoreLogo.png')
New-FrpmLogo -Size 150 -Path (Join-Path $OutputDirectory 'Square150x150Logo.png')
New-FrpmLogo -Size 44 -Path (Join-Path $OutputDirectory 'Square44x44Logo.png')
