<#
.SYNOPSIS
  Builds the Teams app package (zip) from appPackage/, substituting the bot's app id.

.EXAMPLE
  ./scripts/Build-AppPackage.ps1 -BotId 00000000-0000-0000-0000-000000000000
  Produces appPackage/napoleonskake.zip, ready to upload in Teams (Apps > Manage your apps > Upload an app).
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F-]{36}$')]
    [string] $BotId,

    [string] $OutFile = "napoleonskake.zip"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$appPackage = Join-Path $root "appPackage"
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("napoleonskake-" + [Guid]::NewGuid().ToString("N"))

foreach ($icon in "color.png", "outline.png") {
    if (-not (Test-Path (Join-Path $appPackage $icon))) {
        throw "Missing $icon in appPackage/. Run 'node scripts/make-icons.mjs' or add your own icons."
    }
}

New-Item -ItemType Directory -Path $staging | Out-Null
try {
    (Get-Content (Join-Path $appPackage "manifest.json") -Raw).Replace('${{BOT_ID}}', $BotId) |
        Set-Content (Join-Path $staging "manifest.json") -NoNewline
    Copy-Item (Join-Path $appPackage "color.png"), (Join-Path $appPackage "outline.png") $staging

    $zip = Join-Path $appPackage $OutFile
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $zip
    Write-Host "Created $zip"
}
finally {
    Remove-Item -Recurse -Force $staging
}
