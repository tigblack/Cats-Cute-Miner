<#
.SYNOPSIS
  Starts the Unity ad footage capture (AdCaptureTests) detached from the remote session, so a dropped
  connection does not kill it. Progress: Docs\Reports\ads-capture-run.log (ends with "EXIT <code>"),
  per-shot logs in Docs\Reports\ads\*.json, footage in build\adcapture\*.mp4.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools\AdVideo\start_capture.ps1 -Filter "AdCaptureTests.Mine1"
#>
# The filter is quoted for cmd: a regex such as "ClassA|ClassB" would otherwise be split into a pipeline.
param([string]$Filter = 'AdCaptureTests')
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$log = Join-Path $root 'Docs\Reports\ads-capture-run.log'
Remove-Item $log -ErrorAction SilentlyContinue
$cmd = "cmd /c cd /d `"$root`" && powershell -NoProfile -ExecutionPolicy Bypass -File Tools\unity.ps1 -Mode PlayMode -Target Ci -TestFilter `"$Filter`" > `"$log`" 2>&1 & echo EXIT %ERRORLEVEL% >> `"$log`""
$r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $cmd; CurrentDirectory = $root }
"started pid=$($r.ProcessId) rc=$($r.ReturnValue) log=$log"
