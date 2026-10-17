<# Loads the private upload signing data (new-upload-key.ps1) into this process environment only, then runs
build-aab.ps1. Passwords never appear in arguments, output or tracked files.
Keep this file ASCII-only (Windows PowerShell 5.1).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][int]$VersionCode,
    [Parameter(Mandatory=$true)][string]$Output,
    [string]$Package = 'com.multicastgames.catscuteminer'
)
$ErrorActionPreference = 'Stop'
$secret = Join-Path $env:USERPROFILE ('MulticastSigning\' + $Package + '\signing.secret.json')
if (-not (Test-Path -LiteralPath $secret)) { throw "Signing data not found: run Tools/GooglePlay/new-upload-key.ps1 first ($secret)." }
$s = Get-Content -LiteralPath $secret -Raw | ConvertFrom-Json
if ($s.package -ne $Package) { throw 'Signing data belongs to another package.' }
$env:CATMINERS_UPLOAD_KEYSTORE = $s.keystore
$env:CATMINERS_UPLOAD_ALIAS = $s.alias
$env:CATMINERS_UPLOAD_STORE_PASSWORD = $s.storePassword
$env:CATMINERS_UPLOAD_KEY_PASSWORD = $s.keyPassword
$env:CATMINERS_UPLOAD_CERT_SHA256 = $s.certSha256
try {
    & (Join-Path $PSScriptRoot 'build-aab.ps1') -Package $Package -Version $Version -VersionCode $VersionCode -Output $Output
} finally {
    foreach ($n in 'CATMINERS_UPLOAD_KEYSTORE','CATMINERS_UPLOAD_ALIAS','CATMINERS_UPLOAD_STORE_PASSWORD','CATMINERS_UPLOAD_KEY_PASSWORD','CATMINERS_UPLOAD_CERT_SHA256') {
        Remove-Item ("Env:\" + $n) -ErrorAction SilentlyContinue
    }
}
