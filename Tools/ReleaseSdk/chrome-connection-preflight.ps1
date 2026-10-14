$ErrorActionPreference = 'Stop'
$items = @(Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" | Where-Object {
    -not $_.CommandLine -or $_.CommandLine -notmatch '--type='
} | ForEach-Object {
    $explicitProfile = $null
    if ($_.CommandLine -match '--user-data-dir(?:=|\s+)(?:"([^"]+)"|([^\s]+))') {
        $explicitProfile = if ($matches[1]) { $matches[1] } else { $matches[2] }
    }
    [pscustomobject]@{
        processId = $_.ProcessId
        commandLineReadable = [bool]$_.CommandLine
        sessionId = $_.SessionId
        executable = $_.ExecutablePath
        version = if ($_.ExecutablePath -and (Test-Path $_.ExecutablePath)) { (Get-Item $_.ExecutablePath).VersionInfo.ProductVersion } else { $null }
        explicitUserDataDirectory = $explicitProfile
        debuggingPortArgument = [bool]($_.CommandLine -match '--remote-debugging-port')
        debuggingPipeArgument = [bool]($_.CommandLine -match '--remote-debugging-pipe')
    }
})
$defaultProfile = Join-Path $env:LOCALAPPDATA 'Google\Chrome\User Data'
$debugPreferences = @()
$localState = Join-Path $defaultProfile 'Local State'
if (Test-Path -LiteralPath $localState) {
    $state = Get-Content -LiteralPath $localState -Raw | ConvertFrom-Json
    function Find-DebugPreference($node, [string]$path) {
        if ($null -eq $node -or $node -isnot [pscustomobject]) { return }
        foreach ($property in $node.PSObject.Properties) {
            $next = if ($path) { $path + '.' + $property.Name } else { $property.Name }
            if ($next -match '(?i)remote.?debug' -and ($property.Value -is [bool] -or $property.Value -is [int] -or $property.Value -is [long])) {
                [pscustomobject]@{ preference = $next; value = $property.Value }
            }
            if ($property.Value -is [pscustomobject]) { Find-DebugPreference $property.Value $next }
        }
    }
    $debugPreferences = @(Find-DebugPreference $state '')
    $activeProfile = [string]$state.profile.last_used
    if ($activeProfile -and $activeProfile -notmatch '[\\/]' -and $activeProfile -ne '..') {
        $profilePreferences = Join-Path (Join-Path $defaultProfile $activeProfile) 'Preferences'
        if (Test-Path -LiteralPath $profilePreferences) {
            $preferences = Get-Content -LiteralPath $profilePreferences -Raw | ConvertFrom-Json
            $debugPreferences += @(Find-DebugPreference $preferences 'profile')
        }
    }
}
[pscustomobject]@{
    currentUser = [Environment]::UserName
    computerName = [Environment]::MachineName
    defaultProfile = $defaultProfile
    defaultDevToolsActivePortExists = Test-Path (Join-Path $defaultProfile 'DevToolsActivePort')
    remoteDebugPreferences = $debugPreferences
    browsers = $items
} | ConvertTo-Json -Depth 5
