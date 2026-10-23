<# Read-only Windows inventory. No Unity launch, installs, settings writes or service calls. #>
[CmdletBinding()]
param([string]$ProjectPath = '',
      [string]$UnityExe = $env:UNITY_EXE, [switch]$CheckDevices)
$ErrorActionPreference = 'Stop'
if (-not $ProjectPath) { $ProjectPath = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
$root = (Resolve-Path -LiteralPath $ProjectPath).Path
$version = ([regex]::Match((Get-Content (Join-Path $root 'ProjectSettings\ProjectVersion.txt') -Raw), 'm_EditorVersion:\s*(\S+)')).Groups[1].Value
if ($UnityExe) {
    if (-not (Test-Path -LiteralPath $UnityExe -PathType Leaf)) { throw 'UNITY_EXE/UnityExe does not point to an existing editor.' }
} else {
    $UnityExe = @("$env:ProgramFiles\Unity\Hub\Editor\$version\Editor\Unity.exe", "C:\Unity\$version\Editor\Unity.exe", 'C:\Unity\Editor\Unity.exe') |
        Where-Object { (Test-Path -LiteralPath $_ -PathType Leaf) -and (Get-Item -LiteralPath $_).VersionInfo.ProductVersion -like "$version*" } | Select-Object -First 1
}
function Read-Pref([string]$prefix) {
    $key = 'HKCU:\Software\Unity Technologies\Unity Editor 5.x'
    if (Test-Path $key) {
        $item = Get-Item $key
        foreach ($name in $item.Property) {
            if ($name -match ('^' + [regex]::Escape($prefix) + '(?:_h\d+)?$')) {
                $value = $item.GetValue($name)
                if ($value -is [byte[]]) { $value = [Text.Encoding]::UTF8.GetString($value).TrimEnd([char]0) }
                return $value
            }
        }
    }
    return $null
}
function Tool-Candidate([string]$path, [string]$source, [string]$marker) {
    if (-not $path) { return }
    # Only local filesystem paths are safe to print; never emit URLs/credential strings.
    if ($path -notmatch '^[A-Za-z]:[\\/]' -or $path -match '[\r\n]') { return }
    [ordered]@{ path = $path; source = $source; usable_files_present = (Test-Path -LiteralPath (Join-Path $path $marker)) }
}
$player = if ($UnityExe) { Join-Path (Split-Path -Parent $UnityExe) 'Data\PlaybackEngines\AndroidPlayer' } else { $null }
$toolsets = [ordered]@{}
foreach ($spec in @(
    @{ name='SDK'; pref='AndroidSdkRoot'; flag='SdkUseEmbedded'; envs=@('ANDROID_SDK_ROOT','ANDROID_HOME'); marker='platform-tools\adb.exe' },
    @{ name='NDK'; pref='AndroidNdkRoot'; flag='NdkUseEmbedded'; envs=@('ANDROID_NDK_ROOT','ANDROID_NDK_HOME'); marker='source.properties' },
    @{ name='OpenJDK'; pref='JdkPath'; flag='JdkUseEmbedded'; envs=@('JAVA_HOME'); marker='bin\java.exe' }
)) {
    $candidates = @()
    if ($player) { $candidates += Tool-Candidate (Join-Path $player $spec.name) 'bundled' $spec.marker }
    $candidates += Tool-Candidate ([string](Read-Pref $spec.pref)) 'EditorPrefs' $spec.marker
    foreach ($name in $spec.envs) { $candidates += Tool-Candidate ([Environment]::GetEnvironmentVariable($name)) $name $spec.marker }
    $toolsets[$spec.name] = [ordered]@{ embedded_preference = (Read-Pref $spec.flag); candidates = @($candidates); available = @($candidates | Where-Object usable_files_present).Count -gt 0 }
}
$platforms = @()
$revisions = @()
foreach ($sdk in $toolsets.SDK.candidates) {
    $platformDir = Join-Path $sdk.path 'platforms'
    if (Test-Path -LiteralPath $platformDir) {
        $platforms += Get-ChildItem -LiteralPath $platformDir -Directory | ForEach-Object { [ordered]@{ sdk=$sdk.path; platform=$_.Name; android_jar_present=(Test-Path (Join-Path $_.FullName 'android.jar')) } }
    }
}
foreach ($name in @('NDK','OpenJDK')) {
    foreach ($candidate in $toolsets[$name].candidates) {
        $file = Join-Path $candidate.path $(if ($name -eq 'NDK') { 'source.properties' } else { 'release' })
        if (Test-Path -LiteralPath $file) {
            $match = [regex]::Match((Get-Content -LiteralPath $file -Raw), $(if ($name -eq 'NDK') { '(?m)^Pkg.Revision\s*=\s*([\d.]+)' } else { '(?m)^JAVA_VERSION="([\w.\-+]+)"' }))
            $revisions += [ordered]@{ tool=$name; path=$candidate.path; version=$match.Groups[1].Value }
        }
    }
}
$devices = [ordered]@{ checked=$false; connected=0; unauthorized=0; offline=0; note='Not requested or adb unavailable.' }
if ($CheckDevices) {
    $adb = $toolsets.SDK.candidates | Where-Object usable_files_present | Select-Object -First 1
    if ($adb) {
        $adbPath = Join-Path $adb.path 'platform-tools\adb.exe'
        # Avoid starting the adb server in this read-only probe. Query only a running server.
        $port = Test-NetConnection -ComputerName 127.0.0.1 -Port 5037 -InformationLevel Quiet -WarningAction SilentlyContinue
        if ($port) {
            $lines = @(& $adbPath devices 2>$null)
            if ($LASTEXITCODE -eq 0) {
                $devices.checked=$true; $devices.note='Serials omitted.'
                foreach ($state in @('device','unauthorized','offline')) {
                    $field = if ($state -eq 'device') { 'connected' } else { $state }
                    $devices[$field] = @($lines | Where-Object { $_ -match ("\s" + $state + '$') }).Count
                }
            } else { $devices.note='adb query failed; no raw output emitted.' }
        } else { $devices.note='adb server not running; skipped to keep probe read-only.' }
    }
}
$settings = Get-Content (Join-Path $root 'ProjectSettings\ProjectSettings.asset') -Raw
$safeSettings = [ordered]@{}
foreach ($field in @('bundleVersion','AndroidBundleVersionCode','AndroidTargetSdkVersion','activeInputHandler')) {
    $safeSettings[$field] = [regex]::Match($settings, ('(?m)^\s*' + $field + ':\s*([^\r\n]*)')).Groups[1].Value
}
$safeSettings.androidPackage = [regex]::Match($settings, '(?m)^\s{4}Android:\s*([\w.]+)\s*$').Groups[1].Value
$inputs = [ordered]@{}
foreach ($path in @('Assets\Game\Scenes\CoreGame.unity','Assets\Game\Input\CatMinersInput.inputactions','Assets\Game\Data\Source\json-data.json','Packages\manifest.json','Packages\packages-lock.json')) {
    $inputs[$path] = Test-Path -LiteralPath (Join-Path $root $path)
}
$scenes = @()
$buildSettings = Get-Content (Join-Path $root 'ProjectSettings\EditorBuildSettings.asset') -Raw
foreach ($match in [regex]::Matches($buildSettings, '(?m)^\s*- enabled:\s*([01])\r?\n\s*path:\s*([^\r\n]+)')) {
    $path = $match.Groups[2].Value.Trim()
    $scenes += [ordered]@{ path=$path; enabled=($match.Groups[1].Value -eq '1'); present=(Test-Path -LiteralPath (Join-Path $root $path)) }
}
$providerDependencies = @()
$manifest = Get-Content (Join-Path $root 'Packages\manifest.json') -Raw | ConvertFrom-Json
foreach ($property in $manifest.dependencies.psobject.Properties) {
    if ($property.Name -match 'appmetrica|adjust|facebook|applovin|purchasing|payments') {
        $providerDependencies += [ordered]@{ package=$property.Name; version=$(if ([string]$property.Value -match '^[\w.\-]+$') { [string]$property.Value } else { 'non-registry dependency; value redacted' }) }
    }
}
$blockers = @('Accepted release package/version/code and existing upload signing key must be supplied.', 'Provider integration/configuration must pass BuildAndroid.IReleaseGate; no readiness inferred from package presence.', 'Final artifact signature, merged manifest, Billing >=8 and every native library 16 KB alignment remain unverified.')
foreach ($name in $toolsets.Keys) { if (-not $toolsets[$name].available) { $blockers += "$name not found in bundled, external EditorPrefs or environment candidates." } }
if (-not ($player -and (Test-Path -LiteralPath $player))) { $blockers += 'AndroidPlayer module missing.' }
if (-not @($platforms | Where-Object { $_.platform -eq 'android-36' -and $_.android_jar_present }).Count) { $blockers += 'API 36 platform not found.' }
if ($settings -match 'm_BuildTargetIcons: \[\]') { $blockers += 'No platform icons serialized; inspect Android/default icons in Unity.' }
[ordered]@{ scope='read_only_machine_inventory'; project=$root; requiredUnity=$version; unityExe=$UnityExe;
    installedUnity=$(if ($UnityExe) { (Get-Item -LiteralPath $UnityExe).VersionInfo.ProductVersion } else { $null });
    androidModulePresent=[bool]($player -and (Test-Path -LiteralPath $player)); tools=$toolsets; versions=$revisions;
    platforms=$platforms; devices=$devices; settings=$safeSettings;
    buildInputs=$inputs; configuredScenes=$scenes; providerPackageHints=$providerDependencies;
    coreScenePresent=(Test-Path (Join-Path $root 'Assets\Game\Scenes\CoreGame.unity'));
    unityProcessCount=@(Get-Process Unity -ErrorAction SilentlyContinue).Count; blockers=$blockers;
    limitations=@('Candidates do not establish effective Unity External Tools selection or toolchain compatibility.', 'No Unity build, signing secrets, provider values or device serials are emitted.')
} | ConvertTo-Json -Depth 8
