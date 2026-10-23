[CmdletBinding()]
param(
    [string]$UnityExe = 'C:\Unity\Editor\Unity.exe',
    [ValidateSet('Configure', 'CheckAndroidApi')][string]$Mode = 'Configure'
)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path -LiteralPath $UnityExe)) { throw 'Required Unity Editor not found.' }
$jobs = @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object {
    $_.CommandLine -match '(?i)(?:^|\s)-batchmode(?:\s|$)' -or
    ($_.CommandLine -and $_.CommandLine.Contains($project))
})
if ($jobs.Count) { throw 'Unity is already using this project or running another batch job.' }
$lock = Join-Path $project 'Temp\UnityLockfile'
if (Test-Path -LiteralPath $lock) {
    try { $stream = [IO.File]::Open($lock, 'Open', 'ReadWrite', 'None'); $stream.Dispose() }
    catch { throw 'Project is locked; close the existing Editor before applying configuration.' }
}
$reports = Join-Path $project 'Docs\Reports'
New-Item -ItemType Directory -Force -Path $reports | Out-Null
$logName = if ($Mode -eq 'CheckAndroidApi') { 'google-play-android-api.log' } else { 'google-play-native-configuration.log' }
$method = if ($Mode -eq 'CheckAndroidApi') { 'CatMiners.Editor.GooglePlay.AndroidPlayerApiCheck.RunBatch' } else { 'CatMiners.Editor.GooglePlay.GooglePlayProjectConfiguration.RunBatch' }
$log = Join-Path $reports $logName
$arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $project + '"'),
    '-executeMethod', $method,
    '-logFile', ('"' + $log + '"'))
$process = Start-Process -FilePath $UnityExe -ArgumentList $arguments -PassThru -Wait
if ($process.ExitCode -ne 0) { throw "Unity $Mode failed, exit $($process.ExitCode). Inspect $logName." }
if (Select-String -LiteralPath $log -Pattern 'error CS\d+' -Quiet) { throw 'Unity compilation failed; inspect the configuration log.' }
Write-Output "Unity $Mode completed; artifact verification remains pending."
