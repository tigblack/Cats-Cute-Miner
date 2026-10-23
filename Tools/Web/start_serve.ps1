<#
.SYNOPSIS
  Starts Tools\Web\serve.cjs detached from the remote session (it keeps running after the connection closes).
  Output: Docs\Reports\web-serve.log. Stop: Stop-Process -Id <pid printed here>, or
  Get-CimInstance Win32_Process -Filter "Name='node.exe'" | Where-Object CommandLine -like '*serve.cjs*' | ForEach-Object { Stop-Process -Id $_.ProcessId }
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Web\start_serve.ps1 -Playable
#>
param([int]$Port = 8095, [switch]$Playable)
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$log = Join-Path $root 'Docs\Reports\web-serve.log'
$node = (Get-Command node -ErrorAction Stop).Source
$flags = "--port $Port" + $(if ($Playable) { ' --playable' } else { '' })
$cmd = "cmd /c `"`"$node`" Tools\Web\serve.cjs $flags > `"$log`" 2>&1`""
$r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $cmd; CurrentDirectory = $root }
Start-Sleep -Seconds 2
$server = Get-CimInstance Win32_Process -Filter "ParentProcessId=$($r.ProcessId) AND Name='node.exe'"
"started cmd pid=$($r.ProcessId) node pid=$($server.ProcessId) rc=$($r.ReturnValue) log=$log"
Get-Content $log -ErrorAction SilentlyContinue
