<#
.SYNOPSIS
  Starts the playable WebGL build (Tools\unity.ps1 -Mode BuildPlayable -Target Ci) detached from the remote session, so a
  dropped connection does not kill it. Progress: Docs\Reports\build-playable-run.log (ends with "EXIT <code>"),
  result: Docs\Reports\build-playable.json, files: Builds\Playable.
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Web\start_build_playable.ps1
#>
param([ValidateSet('Ci', 'Real', 'Auto')][string]$Target = 'Ci')
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$log = Join-Path $root 'Docs\Reports\build-playable-run.log'
Remove-Item $log -ErrorAction SilentlyContinue
# Delayed expansion (/v:on, !ERRORLEVEL!) records the exit code of unity.ps1, not the value before the line ran.
$cmd = "cmd /v:on /c cd /d `"$root`" && powershell -NoProfile -ExecutionPolicy Bypass -File Tools\unity.ps1 -Mode BuildPlayable -Target $Target > `"$log`" 2>&1 & echo EXIT !ERRORLEVEL! >> `"$log`""
$r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $cmd; CurrentDirectory = $root }
"started pid=$($r.ProcessId) rc=$($r.ReturnValue) log=$log"
