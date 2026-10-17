<#
Read-only build-machine inventory for MultiCode/Windows.
Does not install SDKs, change Unity settings, print credentials or publish.
Run on the user's PC, not on the Linux hub.
#>
[CmdletBinding()]
param(
    [string]$ProjectPath = '.',
    [string]$ReferencePath,
    [switch]$CheckDevices
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$root = (Resolve-Path -LiteralPath $ProjectPath).Path

function Read-Setting([string]$Text, [string]$Name) {
    $found = [regex]::Match($Text, '(?m)^\s*' + [regex]::Escape($Name) + ':\s*([^\r\n]*)')
    if ($found.Success) { return $found.Groups[1].Value.Trim() }
    return $null
}

function File-Present([string]$Path) {
    return [bool](Test-Path -LiteralPath $Path)
}

$settingsPath = Join-Path $root 'ProjectSettings\ProjectSettings.asset'
$versionPath = Join-Path $root 'ProjectSettings\ProjectVersion.txt'
$settings = ''
$unityVersion = $null
if (File-Present $settingsPath) { $settings = Get-Content -LiteralPath $settingsPath -Raw }
if (File-Present $versionPath) {
    $unityVersion = Read-Setting (Get-Content -LiteralPath $versionPath -Raw) 'm_EditorVersion'
}

$toolsFound = [ordered]@{}
foreach ($tool in @('unity', 'dotnet', 'python', 'adb')) {
    $command = Get-Command $tool -ErrorAction SilentlyContinue | Select-Object -First 1
    $toolsFound[$tool] = if ($command) { $command.Source } else { $null }
}

$editorDirectory = $null
if ($unityVersion) {
    foreach ($edition in @(($unityVersion + '-x86_64'), $unityVersion)) {
        $candidate = Join-Path $env:ProgramFiles ('Unity\Hub\Editor\' + $edition + '\Editor')
        if (File-Present (Join-Path $candidate 'Unity.exe')) { $editorDirectory = $candidate; break }
    }
}
$androidDirectory = if ($editorDirectory) { Join-Path $editorDirectory 'Data\PlaybackEngines\AndroidPlayer' } else { $null }
$androidInstalled = $androidDirectory -and (File-Present $androidDirectory)
$adbPath = $toolsFound['adb']
if (-not $adbPath -and $androidInstalled) {
    $candidate = Join-Path $androidDirectory 'SDK\platform-tools\adb.exe'
    if (File-Present $candidate) { $adbPath = $candidate }
}
$platforms = @()
if ($androidInstalled) {
    $platformDirectory = Join-Path $androidDirectory 'SDK\platforms'
    if (File-Present $platformDirectory) {
        $platforms = @(Get-ChildItem -LiteralPath $platformDirectory -Directory | Select-Object -ExpandProperty Name)
    }
}

# Read only safe, public settings. Never serialize the whole settings/manifest.
$packageCandidates = @()
$identifierBlock = [regex]::Match($settings, '(?ms)^\s{2}applicationIdentifier:\s*\r?\n(?<block>(?:\s{4}[^\r\n]*\r?\n)+)')
if ($identifierBlock.Success) {
    $androidIdentifier = [regex]::Match($identifierBlock.Groups['block'].Value, '(?m)^\s*Android:\s*(\S+)')
    if ($androidIdentifier.Success) { $packageCandidates += $androidIdentifier.Groups[1].Value }
}
$dependencies = @()
$manifestPath = Join-Path $root 'Packages\manifest.json'
if (File-Present $manifestPath) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.dependencies) {
        $dependencies = @($manifest.dependencies.psobject.Properties | Where-Object {
            $_.Name -match 'appmetrica|adjust|facebook|applovin|purchasing|multicastgames\.payments|external-dependency-manager|analytics|unity\.ads'
        } | ForEach-Object {
            $versionValue = [string]$_.Value
            # A Git dependency URL may contain embedded credentials or query tokens.
            if ($versionValue -match '^[\w.\-]+$') {
                @{ package = $_.Name; version = $versionValue }
            } else {
                @{ package = $_.Name; version = 'non-registry dependency; inspect locally without exposing credentials' }
            }
        })
    }
}

$reference = $null
if ($ReferencePath) {
    if (-not (File-Present $ReferencePath)) {
        $reference = [ordered]@{ path = $ReferencePath; available = $false; note = 'Reference unavailable; target-project inventory continues.' }
    } else {
    $referenceRoot = (Resolve-Path -LiteralPath $ReferencePath).Path
    $assemblyRoot = Join-Path $referenceRoot 'Library\ScriptAssemblies'
    $reference = [ordered]@{
        path = $referenceRoot
        available = $true
        compile_references = [ordered]@{
            appmetrica = (File-Present (Join-Path $assemblyRoot 'AppMetrica.dll'))
            adjust = (File-Present (Join-Path $assemblyRoot 'AdjustSdk.Scripts.dll'))
            max = (File-Present (Join-Path $assemblyRoot 'MaxSdk.Scripts.dll'))
            meta = (File-Present (Join-Path $referenceRoot 'Assets\FacebookSDK\Plugins\Facebook.Unity.dll'))
        }
        note = 'Cached assemblies can validate signatures; they do not prove target-app installation or Android runtime.'
    }
    }
}

$devices = [ordered]@{ checked = $false; adb_path = $adbPath; connected = $null; unauthorized = $null; error = $null }
if ($CheckDevices -and $adbPath) {
    try {
        $deviceLines = @(& $adbPath devices 2>$null)
        if ($LASTEXITCODE -ne 0) { throw 'adb devices failed' }
        $devices.checked = $true
        $devices.connected = @($deviceLines | Where-Object { $_ -match '\sdevice$' }).Count
        $devices.unauthorized = @($deviceLines | Where-Object { $_ -match '\sunauthorized$' }).Count
    } catch {
        $devices.error = 'Device inventory failed; inspect adb/USB setup on the PC.'
    }
}

$buildScripts = @()
$editorCodePath = Join-Path $root 'Assets\_Project\Editor'
if (File-Present $editorCodePath) {
    $buildScripts = @(Get-ChildItem -LiteralPath $editorCodePath -File -Filter '*Build*.cs' | ForEach-Object { $_.Name })
}

$configuredScenes = @()
$buildSettingsPath = Join-Path $root 'ProjectSettings\EditorBuildSettings.asset'
if (File-Present $buildSettingsPath) {
    $buildSettingsText = Get-Content -LiteralPath $buildSettingsPath -Raw
    foreach ($scene in [regex]::Matches($buildSettingsText, '(?m)^\s*-\s*enabled:\s*([01])\s*\r?\n\s*path:\s*([^\r\n]*)')) {
        $scenePath = $scene.Groups[2].Value.Trim().Trim('"')
        $configuredScenes += [ordered]@{
            path = $scenePath
            enabled = ($scene.Groups[1].Value -eq '1')
            exists_on_build_machine = ($scenePath -ne '' -and (File-Present (Join-Path $root $scenePath)))
        }
    }
}

[ordered]@{
    scope = 'read_only_pc_preflight'
    runtime_readiness = 'not_assessed'
    project_path = $root
    unity_version = $unityVersion
    editor_directory = $editorDirectory
    running_unity_process_count = @(Get-Process Unity -ErrorAction SilentlyContinue).Count
    android_module_present = [bool]$androidInstalled
    android_sdk_platforms = $platforms
    public_settings = [ordered]@{
        company = (Read-Setting $settings 'companyName')
        product = (Read-Setting $settings 'productName')
        version_name = (Read-Setting $settings 'bundleVersion')
        version_code = (Read-Setting $settings 'AndroidBundleVersionCode')
        explicit_android_package_candidates = $packageCandidates
        custom_upload_keystore_configured = ((Read-Setting $settings 'androidUseCustomKeystore') -eq '1')
    }
    dependencies = $dependencies
    project_build_script_names = $buildScripts
    build_inputs = [ordered]@{
        package_manifest_present = (File-Present $manifestPath)
        package_lock_present = (File-Present (Join-Path $root 'Packages\packages-lock.json'))
        configured_scenes = $configuredScenes
        note = 'Read-only configured scene check; custom build scripts may choose other scenes/assets. Verify their actual inputs too.'
    }
    tools = $toolsFound
    devices = $devices
    reference = $reference
    limitations = @(
        'Editor settings require confirmation through PlayerSettings and the built manifest.',
        'No signing secrets, provider configuration values, binary contents or device serials are emitted.',
        'No SDK initialization, service access, release build or Google Play write is performed.'
    )
} | ConvertTo-Json -Depth 8
