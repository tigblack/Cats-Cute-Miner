<# Creates the Google Play upload key of this game ONCE, outside the project (not synced, not in Git).
Never overwrites an existing key. Prints only public data (path, alias, certificate SHA-256).
Passwords are random and stored in signing.secret.json next to the keystore (user profile ACL);
build-with-signing.ps1 loads them into the build process environment.
Keep this file ASCII-only (Windows PowerShell 5.1).
#>
[CmdletBinding()]
param([string]$Package = 'com.multicastgames.catscuteminer', [string]$Alias = 'upload')
$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:USERPROFILE ('MulticastSigning\' + $Package)
$jks = Join-Path $dir 'upload.jks'
$secret = Join-Path $dir 'signing.secret.json'
if ((Test-Path -LiteralPath $jks) -or (Test-Path -LiteralPath $secret)) { throw "Upload key already exists in $dir; refusing to overwrite." }
$jdk = 'C:\Unity\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK'
$keytool = Join-Path $jdk 'bin\keytool.exe'
if (-not (Test-Path -LiteralPath $keytool)) { throw 'keytool not found in Unity bundled OpenJDK.' }
New-Item -ItemType Directory -Force -Path $dir | Out-Null

$chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
$bytes = New-Object byte[] 32
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$password = -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })
$env:CATMINERS_KEYGEN_PASS = $password
# keytool prints progress to stderr: native stderr must not become a terminating error here.
$ErrorActionPreference = 'Continue'
try {
    # PKCS12: one password for the store and the key (keytool ignores a different -keypass for PKCS12).
    $out = & $keytool -genkeypair -keystore $jks -storetype PKCS12 -alias $Alias -keyalg RSA -keysize 2048 -validity 10000 `
        -dname 'CN=Cats Cute Miner Upload, O=Multicast Games Limited' -storepass:env CATMINERS_KEYGEN_PASS -keypass:env CATMINERS_KEYGEN_PASS 2>&1
    if ($LASTEXITCODE -ne 0) { throw ('keytool genkeypair failed: ' + (($out | Out-String) -replace [regex]::Escape($password), '***')) }
    $list = & $keytool -list -v -keystore $jks -storetype PKCS12 -alias $Alias -storepass:env CATMINERS_KEYGEN_PASS 2>&1 | Out-String
    $m = [regex]::Match($list, 'SHA256:\s*([0-9A-F:]{95})')
    if (-not $m.Success) { throw 'Could not read the certificate SHA-256 fingerprint.' }
    $sha = $m.Groups[1].Value
} finally {
    Remove-Item Env:\CATMINERS_KEYGEN_PASS -ErrorAction SilentlyContinue
    $ErrorActionPreference = 'Stop'
}
$data = [ordered]@{
    package = $Package; keystore = $jks; storeType = 'PKCS12'; alias = $Alias
    storePassword = $password; keyPassword = $password
    certSha256 = ($sha -replace ':', ''); certSha256Colon = $sha
    createdUtc = (Get-Date).ToUniversalTime().ToString('o')
}
$data | ConvertTo-Json | Set-Content -LiteralPath $secret -Encoding UTF8
[ordered]@{ keystore = $jks; secret = $secret; alias = $Alias; certSha256 = $sha } | ConvertTo-Json
