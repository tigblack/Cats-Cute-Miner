$ErrorActionPreference = 'Stop'
$file = Join-Path $env:LOCALAPPDATA 'Google\Chrome\User Data\DevToolsActivePort'
$reachable = $false
$valid = $false
if (Test-Path -LiteralPath $file) {
    $first = Get-Content -LiteralPath $file -TotalCount 1
    [int]$port = 0
    $valid = [int]::TryParse($first, [ref]$port) -and $port -gt 0 -and $port -le 65535
    if ($valid) {
        $client = [Net.Sockets.TcpClient]::new()
        try {
            $pending = $client.BeginConnect('127.0.0.1', $port, $null, $null)
            if ($pending.AsyncWaitHandle.WaitOne(1000)) {
                $client.EndConnect($pending)
                $reachable = $client.Connected
            }
        } catch { $reachable = $false }
        finally { $client.Dispose() }
    }
}
[ordered]@{
    checkedAtUtc = [DateTime]::UtcNow.ToString('o')
    nativeEndpointFileExists = (Test-Path -LiteralPath $file)
    nativeEndpointPortValid = $valid
    tcpReachable = $reachable
    browserConnected = $false
    cookiesRead = $false
} | ConvertTo-Json
