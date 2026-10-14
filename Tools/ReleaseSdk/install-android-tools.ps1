param([string]$UnityExe = 'C:\Unity\Editor\Unity.exe')
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$metadata = Get-Content (Join-Path $root 'release\google-play\evidence\unity-release-metadata.json') -Raw | ConvertFrom-Json
$download = $metadata.results[0].downloads | Where-Object { $_.platform -eq 'WINDOWS' -and $_.architecture -eq 'X86_64' } | Select-Object -First 1
$android = $download.modules | Where-Object id -eq 'android' | Select-Object -First 1
$player = Join-Path (Split-Path -Parent $UnityExe) 'Data\PlaybackEngines\AndroidPlayer'
if (-not (Test-Path $player)) { throw 'Install the exact-version AndroidPlayer module first.' }
$stage = Join-Path $root 'Temp\google-play-android-tools'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Find-Module($node, [string]$id) {
    if ($node.id -eq $id) { return $node }
    foreach ($child in $node.subModules) { $found = Find-Module $child $id; if ($found) { return $found } }
}
$jobs = @(
    @{ id='android-open-jdk-17.0.9+9'; sub=''; target='OpenJDK'; marker='bin\java.exe' },
    @{ id='android-sdk-ndk-tools'; sub='tools'; target='SDK\tools'; marker='bin\sdkmanager.bat' },
    @{ id='android-ndk-r27c'; sub='android-ndk-r27c'; target='NDK'; marker='source.properties' },
    @{ id='cmake-3.22.1'; sub=''; target='SDK\cmake\3.22.1'; marker='bin\cmake.exe' },
    @{ id='android-sdk-build-tools-36.0.0'; sub='android-16'; target='SDK\build-tools\36.0.0'; marker='aapt2.exe' },
    @{ id='android-sdk-platform-tools-36.0.0'; sub='platform-tools'; target='SDK\platform-tools'; marker='adb.exe' },
    @{ id='android-sdk-platforms-36'; sub='android-36'; target='SDK\platforms\android-36'; marker='android.jar' },
    @{ id='android-sdk-command-line-tools-16.0'; sub='cmdline-tools'; target='SDK\cmdline-tools\16.0'; marker='bin\sdkmanager.bat' }
)
$receipts = @()
$reportPath = Join-Path $root 'release\google-play\evidence\android-tools-install.json'
$previous = if (Test-Path $reportPath) { (Get-Content $reportPath -Raw | ConvertFrom-Json).components } else { @() }
foreach ($job in $jobs) {
    $module = Find-Module $android $job.id
    if (-not $module -or $module.type -ne 'ZIP' -or $module.url -notmatch '^https://(download\.unity3d\.com|dl\.google\.com)/') { throw ('Invalid official module source: ' + $job.id) }
    $target = Join-Path $player $job.target
    if (Test-Path (Join-Path $target $job.marker)) {
        $prior = @($previous | Where-Object { $_.id -eq $job.id -and $_.path -eq $target }) | Select-Object -First 1
        if ($prior) { $receipts += $prior }
        else { $receipts += [ordered]@{ id=$job.id; status='existing_marker_present'; path=$target } }
        continue
    }
    if (Test-Path $target) { throw ('Preserving an existing incomplete tool directory; inspect before replacement: ' + $target) }
    $zip = Join-Path $stage ($job.id.Replace('+','_') + '.zip')
    Write-Output ('Downloading official Unity module: ' + $job.id)
    if (-not (Test-Path $zip)) { Invoke-WebRequest -Uri $module.url -OutFile ($zip + '.partial') -UseBasicParsing -TimeoutSec 180; Move-Item ($zip + '.partial') $zip }
    $actualHash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($module.integrity -match '^sha384-(.+)$') {
        $sha = [Security.Cryptography.SHA384]::Create()
        $stream = [IO.File]::OpenRead($zip)
        try { $actual = [Convert]::ToBase64String($sha.ComputeHash($stream)) } finally { $stream.Dispose(); $sha.Dispose() }
        if ($actual -ne $Matches[1]) { throw ('Official archive integrity mismatch: ' + $job.id) }
    }
    $unpacked = Join-Path $stage ([Guid]::NewGuid().ToString('N'))
    Write-Output ('Extracting: ' + $job.id)
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $unpacked)
    $payload = if ($job.sub) { Join-Path $unpacked $job.sub } else { $unpacked }
    if (-not (Test-Path (Join-Path $payload $job.marker))) { throw ('Archive layout/marker mismatch: ' + $job.id) }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Move-Item -LiteralPath $payload -Destination $target
    if (Test-Path $unpacked) { Remove-Item $unpacked -Recurse -Force }
    $receipts += [ordered]@{ id=$job.id; status='installed'; path=$target; source=$module.url; archive_sha256=$actualHash }
    $report = [ordered]@{ unity_version='6000.3.11f1'; status='in_progress'; components=$receipts; licenses_accepted_by_script=$false }
    $report | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $root 'release\google-play\evidence\android-tools-install.json') -Encoding UTF8
}
# Installation does not fabricate license acceptance or alter an existing toolchain.
# Any license step requested by the actual build is handled explicitly later.
[ordered]@{ unity_version='6000.3.11f1'; status='installed_pending_effective_unity_and_license_checks'; components=$receipts; licenses_accepted_by_script=$false } |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $root 'release\google-play\evidence\android-tools-install.json') -Encoding UTF8
Write-Output 'Android tools delivered. Verify effective Unity selection and any required SDK license acceptance before building.'
