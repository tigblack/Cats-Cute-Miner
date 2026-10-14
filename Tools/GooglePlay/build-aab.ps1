<# Local only. Supply approved identity and an existing upload key; never creates keys or uploads.
Passwords are inherited from CATMINERS_UPLOAD_STORE_PASSWORD / CATMINERS_UPLOAD_KEY_PASSWORD.
Use a private process environment populated by your secret manager, never command-line passwords.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Package,
    [Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+(?:[.-][A-Za-z0-9.-]+)?$')][string]$Version,
    [Parameter(Mandatory=$true)][ValidateRange(1,2100000000)][int]$VersionCode,
    [Parameter(Mandatory=$true)][string]$Output,
    [string]$UnityExe = $env:UNITY_EXE
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ($Package -notmatch '^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$' -or $Package -match 'prototype|example|placeholder') { throw 'Supply the accepted actual release package ID.' }
if ($Version.Length -gt 50) { throw 'Version name exceeds 50 characters.' }
foreach ($name in @('CATMINERS_UPLOAD_KEYSTORE','CATMINERS_UPLOAD_ALIAS','CATMINERS_UPLOAD_STORE_PASSWORD','CATMINERS_UPLOAD_KEY_PASSWORD','CATMINERS_UPLOAD_CERT_SHA256')) {
    if (-not [Environment]::GetEnvironmentVariable($name)) { throw "Required secret/environment reference missing: $name" }
}
if (-not (Test-Path -LiteralPath $env:CATMINERS_UPLOAD_KEYSTORE -PathType Leaf)) { throw 'Existing upload keystore not found.' }
$fingerprint = ($env:CATMINERS_UPLOAD_CERT_SHA256 -replace ':','').ToUpperInvariant()
if ($fingerprint -notmatch '^[A-F0-9]{64}$') { throw 'Expected upload certificate SHA-256 must contain 64 hex digits.' }
$outputPath = [IO.Path]::GetFullPath($Output)
if ([IO.Path]::GetExtension($outputPath) -ne '.aab' -or (Test-Path -LiteralPath $outputPath) -or (Test-Path -LiteralPath ($outputPath + '.build.json'))) { throw 'Use a fresh .aab output path.' }
$probeArgs = @{ ProjectPath=$root }
if ($UnityExe) { $probeArgs.UnityExe=$UnityExe }
$probe = (& (Join-Path $PSScriptRoot 'preflight.ps1') @probeArgs) | ConvertFrom-Json
$UnityExe = $probe.unityExe
if (-not $UnityExe -or $probe.installedUnity -notlike "$($probe.requiredUnity)*") { throw 'Required Unity version unavailable.' }
if (-not $probe.androidModulePresent) { throw 'AndroidPlayer module unavailable.' }
foreach ($name in @('SDK','NDK','OpenJDK')) {
    $tool = $probe.tools.$name
    $source = if ($null -eq $tool.embedded_preference -or [string]$tool.embedded_preference -eq '1') { 'bundled' } else { 'EditorPrefs' }
    $selected = @($tool.candidates | Where-Object { $_.source -eq $source -and $_.usable_files_present })
    if ($selected.Count -ne 1) { throw "Effective $name selection incomplete; inspect preflight and Unity External Tools. Environment candidates are not selected automatically." }
    if ($name -eq 'OpenJDK') { $jdk = $selected[0].path }
    if ($name -eq 'SDK' -and -not (Test-Path (Join-Path $selected[0].path 'platforms\android-36\android.jar'))) { throw 'Effective SDK lacks API 36.' }
}
foreach ($tool in @('jarsigner.exe','keytool.exe')) { if (-not (Test-Path (Join-Path $jdk "bin\$tool"))) { throw "JDK lacks $tool." } }
# Refuse any other batch job, even if it uses a different project or CI mirror.
function Assert-NoBatchUnity {
    $jobs = @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -match '(?i)(?:^|\s)-batchmode(?:\s|$)' })
    if ($jobs.Count) { throw 'Another Unity batch job is running; retry after it finishes.' }
}
function Assert-Unlocked([string]$path) {
    $lock = Join-Path $path 'Temp\UnityLockfile'
    if (Test-Path -LiteralPath $lock) {
        try { $stream=[IO.File]::Open($lock,'Open','ReadWrite','None'); $stream.Dispose() }
        catch { throw 'Google Play CI project is locked; no second Unity process will be started.' }
    }
}
function Quote-Argument([string]$value) {
    if ($value -match '["\r\n]') { throw 'Arguments may not contain quotes or line breaks.' }
    '"' + ($value -replace '(\\+)$','$1$1') + '"'
}
$mutex = [Threading.Mutex]::new($false, 'Local\CatMinersGooglePlayBuild')
$held = $false
$started = $false
try {
    $held = $mutex.WaitOne(0)
    if (-not $held) { throw 'Another Google Play local build owns the build mutex.' }
    Assert-NoBatchUnity
    # Always isolated: the real Editor and its settings are untouched. Never run Setup here.
    $ci = Join-Path $root 'build\google-play-ci'
    Assert-Unlocked $ci
    New-Item -ItemType Directory -Force -Path $ci | Out-Null
    foreach ($dir in @('Assets','Packages','ProjectSettings')) {
        $null = & robocopy (Join-Path $root $dir) (Join-Path $ci $dir) /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1
        if ($LASTEXITCODE -ge 8) { throw "CI mirror failed: $dir" }
    }
    Assert-NoBatchUnity
    Assert-Unlocked $ci
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputPath) | Out-Null
    $log = $outputPath + '.unity.log'
    if (Test-Path -LiteralPath $log) { throw 'Use a new output/log path.' }
    $args = @('-batchmode','-nographics','-projectPath',$ci,'-buildTarget','Android','-executeMethod','CatMiners.Editor.GooglePlay.BuildAndroid.Aab',
        '-logFile',$log,'-catminersPackage',$Package,'-catminersVersion',$Version,'-catminersVersionCode',([string]$VersionCode),'-catminersOutput',$outputPath)
    # Only public values appear on the process command line. Unity inherits signing env privately.
    $started = $true
    $process = Start-Process -FilePath $UnityExe -ArgumentList (($args | ForEach-Object { Quote-Argument $_ }) -join ' ') -PassThru -Wait
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $outputPath)) { throw 'Unity AAB build failed; inspect the local log.' }
    $reportPath = $outputPath + '.build.json'
    if (-not (Test-Path -LiteralPath $reportPath)) { throw 'Fresh BuildReport receipt missing.' }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($report.result -ne 'BuiltPendingArtifactVerification' -or $report.development -or $report.package -ne $Package -or $report.version -ne $Version -or $report.versionCode -ne $VersionCode -or $report.path -ne $outputPath) { throw 'Build receipt disagrees with requested release.' }
    if ((Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $report.sha256) { throw 'Artifact hash disagrees with build receipt.' }
    $verify = @(& (Join-Path $jdk 'bin\jarsigner.exe') '-J-Duser.language=en' '-J-Duser.country=US' -verify $outputPath 2>&1)
    if ($LASTEXITCODE -ne 0 -or -not ($verify -match '^jar verified\.' ) -or ($verify -match 'unsigned entries|jar is unsigned')) { throw 'Final AAB is unsigned, partly unsigned or signature verification failed.' }
    $certOutput = @(& (Join-Path $jdk 'bin\keytool.exe') '-J-Duser.language=en' -printcert -jarfile $outputPath -rfc 2>$null) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read final AAB signing certificate.' }
    $certs = [regex]::Matches($certOutput,'(?s)-----BEGIN CERTIFICATE-----\s*(.*?)\s*-----END CERTIFICATE-----')
    if ($certs.Count -ne 1) { throw 'Expected exactly one upload signing certificate; inspect signing chain locally.' }
    $cert = [Convert]::FromBase64String(($certs[0].Groups[1].Value -replace '\s',''))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $actual = ([BitConverter]::ToString($sha.ComputeHash($cert)) -replace '-','') } finally { $sha.Dispose() }
    if ($actual -ne $fingerprint) { throw 'Artifact signing certificate does not match the expected upload key.' }
    $report | Add-Member -NotePropertyName signatureVerified -NotePropertyValue $true -Force
    $report | Add-Member -NotePropertyName uploadCertificateSha256 -NotePropertyValue $actual -Force
    [IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 8))
    Write-Host "Signed AAB built locally: $outputPath"
    Write-Host 'Upload remains blocked pending final manifest, Billing 8+, every native 16 KB library/packaging, device/provider checks and release confirmation.'
} catch {
    if ($started) {
        foreach ($path in @($outputPath, ($outputPath + '.build.json'))) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force } }
    }
    throw
} finally {
    if ($held) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
