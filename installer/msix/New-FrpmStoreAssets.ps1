param([Parameter(Mandatory = $true)][string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../windows/FrpmIconArtwork.ps1')
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function Save-FrpmLogo([int]$Size, [string]$Name) {
    [IO.File]::WriteAllBytes((Join-Path $OutputDirectory $Name), (New-FrpmPngFrame -Size $Size))
}

Save-FrpmLogo 50 'StoreLogo.png'
Save-FrpmLogo 150 'Square150x150Logo.png'
Save-FrpmLogo 44 'Square44x44Logo.png'
foreach ($scale in @(100, 125, 150, 200, 400)) {
    Save-FrpmLogo ([int](50 * $scale / 100)) "StoreLogo.scale-$scale.png"
    Save-FrpmLogo ([int](44 * $scale / 100)) "Square44x44Logo.scale-$scale.png"
    Save-FrpmLogo ([int](150 * $scale / 100)) "Square150x150Logo.scale-$scale.png"
}
# Windows Start and taskbar use target-size assets; unplated variants suppress
# the system accent-colored square behind the transparent rounded artwork.
foreach ($size in @(16, 20, 24, 30, 32, 36, 40, 44, 48, 60, 64, 72, 80, 96, 128, 256)) {
    Save-FrpmLogo $size "Square44x44Logo.targetsize-$size.png"
    Save-FrpmLogo $size "Square44x44Logo.targetsize-${size}_altform-unplated.png"
    Save-FrpmLogo $size "Square44x44Logo.targetsize-${size}_altform-lightunplated.png"
}
