param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [string]$MakeAppxPath
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^v?(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?$') {
    throw "Store package version '$Version' must use numeric major.minor.patch format, optionally followed by .0."
}

$revision = $Matches[4]
if ($revision -and $revision -ne '0') {
    throw "Microsoft Store requires the MSIX revision to be zero; '$Version' specifies revision $revision. Use a semantic version such as 1.0.1."
}

$packageVersion = "$($Matches[1]).$($Matches[2]).$($Matches[3]).0"
$layout = Join-Path $OutputDirectory 'layout'
$packagePath = Join-Path $OutputDirectory "frpm-$Version-win-x64.msix"
$uploadPath = Join-Path $OutputDirectory "frpm-$Version-win-x64.msixupload"

Remove-Item -LiteralPath $layout -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $layout | Out-Null
Copy-Item -LiteralPath (Join-Path $SourceDirectory 'server') -Destination (Join-Path $layout 'server') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $SourceDirectory 'tray') -Destination (Join-Path $layout 'tray') -Recurse -Force

$manifest = Get-Content -Raw (Join-Path $PSScriptRoot 'AppxManifest.xml')
$manifest.Replace('__PACKAGE_VERSION__', $packageVersion) | Set-Content -LiteralPath (Join-Path $layout 'AppxManifest.xml') -Encoding utf8NoBOM
& (Join-Path $PSScriptRoot 'New-FrpmStoreAssets.ps1') -OutputDirectory (Join-Path $layout 'Assets')

& $MakeAppxPath pack /d $layout /p $packagePath /o
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx failed with exit code $LASTEXITCODE."
}

Compress-Archive -LiteralPath $packagePath -DestinationPath $uploadPath -Force
Write-Output "Created $uploadPath"
