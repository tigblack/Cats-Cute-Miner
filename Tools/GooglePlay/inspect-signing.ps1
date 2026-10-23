<# Verifies the existing upload key and writes only PUBLIC certificate evidence. Never creates or replaces a key. #>
[CmdletBinding()]
param([string]$Package = 'com.multicastgames.catscuteminer')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dir = Join-Path $env:USERPROFILE ('MulticastSigning\' + $Package)
$secret = Join-Path $dir 'signing.secret.json'
if (-not (Test-Path -LiteralPath $secret -PathType Leaf)) { throw 'Upload signing configuration is not present.' }
$s = Get-Content -LiteralPath $secret -Raw | ConvertFrom-Json
if ($s.package -ne $Package -or -not (Test-Path -LiteralPath $s.keystore -PathType Leaf)) { throw 'Upload signing identity/key does not match this game.' }
$keytool = 'C:\Unity\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK\bin\keytool.exe'
if (-not (Test-Path -LiteralPath $keytool)) { throw 'Unity keytool is unavailable.' }
$env:CATMINERS_SIGNING_INSPECT_PASSWORD = $s.storePassword
try {
    # Quoted -J options are required by Windows PowerShell's native argument handling.
    $ErrorActionPreference = 'Continue'
    $out = & $keytool '-J-Duser.language=en' '-J-Duser.country=US' -list -v -keystore $s.keystore -alias $s.alias -storepass:env CATMINERS_SIGNING_INSPECT_PASSWORD 2>&1 | Out-String
    $code = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($code -ne 0) { throw 'Existing keystore could not be opened; diagnostic output suppressed.' }
    $match = [regex]::Match($out, 'SHA256:\s*([0-9A-F:]{95})')
    if (-not $match.Success) { throw 'Could not read the upload certificate fingerprint.' }
    $sha256 = $match.Groups[1].Value -replace ':', ''
    if ($sha256 -ne ($s.certSha256 -replace ':', '')) { throw 'Upload certificate differs from its recorded fingerprint.' }
    $sha1Match = [regex]::Match($out, 'SHA1:\s*([0-9A-F:]{59})')
    if (-not $sha1Match.Success) { throw 'Could not read the upload certificate SHA-1.' }
    $sha1 = $sha1Match.Groups[1].Value -replace ':', ''
    $bytes = New-Object byte[] 20
    for ($i = 0; $i -lt 20; $i++) { $bytes[$i] = [Convert]::ToByte($sha1.Substring($i * 2, 2), 16) }
    $evidence = [ordered]@{
        checkedUtc = (Get-Date).ToUniversalTime().ToString('o')
        package = $Package; keystore = $s.keystore; alias = $s.alias
        secretRef = $secret; certificateSha256 = $sha256; certificateSha1 = $sha1
        metaUploadKeyHash = [Convert]::ToBase64String($bytes)
        status = 'existing_upload_key_verified'
        secretStoredOutsideProject = $true; passwordsEmitted = $false
        playAppSigningCertificateStatus = 'pending_first_Play_Console_release'
        note = 'Meta production also needs the Google Play app-signing certificate hash; the upload key does not replace it.'
    }
    $receipt = Join-Path $root 'release\google-play\evidence\upload-signing.json'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $receipt) | Out-Null
    $json = $evidence | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText($receipt, $json + "`n", (New-Object Text.UTF8Encoding $false))
    Write-Output $json
} finally {
    Remove-Item Env:\CATMINERS_SIGNING_INSPECT_PASSWORD -ErrorAction SilentlyContinue
}
