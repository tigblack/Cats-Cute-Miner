<# Makes the dedicated release Chrome visible in the logged-on Windows desktop.
Never touches the ordinary Chrome profile. No passwords/cookies are copied or read.
The existing agent profile is reused; an interactive task avoids SSH session 0.
#>
[CmdletBinding()]
param([int]$Port = 9333)
$ErrorActionPreference = 'Stop'
$profile = Join-Path $env:LOCALAPPDATA 'MulticastAgentChrome\profile'
$chrome = @("$env:ProgramFiles\Google\Chrome\Application\chrome.exe", "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe", "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe") | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $chrome) { throw 'Chrome is not installed.' }
$desktopUser = (Get-CimInstance Win32_ComputerSystem).UserName
if (-not $desktopUser) { throw 'No Windows desktop user is signed in.' }
$explorerSessions = @(Get-Process explorer -ErrorAction SilentlyContinue | Select-Object -ExpandProperty SessionId -Unique)
if ($explorerSessions.Count -ne 1) { throw 'Cannot unambiguously select the active desktop session.' }
$desktopSession = $explorerSessions[0]
$agentRoots = @(Get-CimInstance Win32_Process | Where-Object {
    $_.Name -eq 'chrome.exe' -and $_.CommandLine -match ("remote-debugging-port=" + $Port + '(?:\s|$)') -and
    $_.CommandLine.Contains($profile) -and $_.CommandLine -notmatch '--type='
})
# Only the dedicated agent browser in an invisible session is restarted.
foreach ($process in $agentRoots) {
    if ($process.SessionId -ne $desktopSession) { Stop-Process -Id $process.ProcessId -Force }
}
if (@($agentRoots | Where-Object SessionId -ne $desktopSession).Count) { Start-Sleep -Seconds 2 }
$arguments = '--remote-debugging-port=' + $Port + ' "--user-data-dir=' + $profile + '" --no-first-run --no-default-browser-check --new-window https://play.google.com/console/u/0/developers/8879742639795815518/app-list?hl=en https://suite.adjust.com/ https://dostupai.aimulticast.org/'
$shell = New-Object -ComObject WScript.Shell
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Cats Cute Miner - Google Play.lnk'
$shortcut = $shell.CreateShortcut($desktopShortcut)
$shortcut.TargetPath = $chrome; $shortcut.Arguments = $arguments; $shortcut.WindowStyle = 1
$shortcut.Description = 'Dedicated Cats Cute Miner publishing browser; sign in here for the release agent.'
$shortcut.Save()
# Replace the old hidden PowerShell/WMI startup shortcut with a direct visible Chrome launch.
$startupShortcut = Join-Path ([Environment]::GetFolderPath('Startup')) 'Multicast Agent Chrome.lnk'
$startup = $shell.CreateShortcut($startupShortcut)
$startup.TargetPath = $chrome; $startup.Arguments = $arguments; $startup.WindowStyle = 1; $startup.Save()
$taskName = 'CatsCuteMinerVisibleReleaseChrome'
$action = New-ScheduledTaskAction -Execute $chrome -Argument $arguments
$principal = New-ScheduledTaskPrincipal -UserId $desktopUser -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName $taskName
for ($n = 0; $n -lt 20; $n++) {
    Start-Sleep -Milliseconds 500
    $visible = @(Get-CimInstance Win32_Process | Where-Object {
        $_.Name -eq 'chrome.exe' -and $_.SessionId -eq $desktopSession -and
        $_.CommandLine -match ("remote-debugging-port=" + $Port + '(?:\s|$)') -and
        $_.CommandLine.Contains($profile) -and $_.CommandLine -notmatch '--type='
    })
    if ($visible.Count) { break }
}
if ($visible.Count -ne 1) { throw 'Interactive launch did not produce one agent browser; use the new desktop shortcut.' }
$window = Get-Process -Id $visible[0].ProcessId
try { $null = $shell.AppActivate($window.Id) } catch { }
[ordered]@{
    status = 'agent_browser_started_in_interactive_desktop_session'
    browserPid = $window.Id; browserSession = $window.SessionId; desktopSession = $desktopSession
    desktopShortcut = $desktopShortcut; profileReused = $true; ordinaryChromeTouched = $false
    windowTitle = $window.MainWindowTitle; windowHandle = [string]$window.MainWindowHandle
} | ConvertTo-Json -Depth 3
