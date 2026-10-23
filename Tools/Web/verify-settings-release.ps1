$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$report = Get-Content (Join-Path $root 'Docs/Reports/build-web.json') -Raw | ConvertFrom-Json
$sha = [Security.Cryptography.SHA256]::Create()
$client = New-Object Net.WebClient
$files = @()
try {
  foreach ($file in $report.files) {
    $local = [IO.File]::ReadAllBytes((Join-Path $root ('Builds/Web/' + $file.name)))
    $remote = $client.DownloadData(('https://tigblack.github.io/Cat-Miners/' + $file.name + '?verify=d6d23eb'))
    if ($file.name -eq 'index.html') {
      $local = [Text.Encoding]::UTF8.GetBytes(([Text.Encoding]::UTF8.GetString($local) -replace "`r`n", "`n"))
      $remote = [Text.Encoding]::UTF8.GetBytes(([Text.Encoding]::UTF8.GetString($remote) -replace "`r`n", "`n"))
    }
    $expected = [BitConverter]::ToString($sha.ComputeHash($local)).Replace('-', '')
    $actual = [BitConverter]::ToString($sha.ComputeHash($remote)).Replace('-', '')
    if ($actual -ne $expected) { throw ('Published file differs from build: ' + $file.name) }
    $files += [ordered]@{name=$file.name; bytes=$remote.Length; sha256=$actual; matches=$true}
  }
  $result = [ordered]@{site='https://tigblack.github.io/Cat-Miners/'; commit='d6d23ebcd7a626e25d4bd677872d5c41c19e8587'; status='passed'; files=$files; checkedAtUtc=(Get-Date).ToUniversalTime().ToString('o')}
  [IO.File]::WriteAllText((Join-Path $root 'Docs/Reports/github-pages/update-2026-10-13-settings/published-files.json'), ($result | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding $false))
  Write-Host ('Published files match build: ' + $files.Count)
} finally { $client.Dispose(); $sha.Dispose() }
