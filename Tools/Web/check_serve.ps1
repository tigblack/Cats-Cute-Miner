# Requests URLs from a running Tools/Web/serve.cjs and prints status and headers (body is read, not shown).
# Example: powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Web/check_serve.ps1 -Port 8095 -Paths /Cat-Miners/playable/
param([int]$Port = 8095, [string[]]$Paths)
foreach ($p in $Paths) {
  $url = "http://localhost:$Port$p"
  try {
    $req = [System.Net.HttpWebRequest]::Create($url)
    $req.AllowAutoRedirect = $false
    $req.Method = 'GET'
    $resp = $req.GetResponse()
  } catch [System.Net.WebException] { $resp = $_.Exception.Response }
  if (-not $resp) { "$p -> no response"; continue }
  $stream = $resp.GetResponseStream(); $buf = New-Object byte[] 65536; $n = 0
  while (($r = $stream.Read($buf, 0, $buf.Length)) -gt 0) { $n += $r }
  "{0} -> {1} type='{2}' encoding='{3}' length={4} read={5} location='{6}'" -f $p, [int]$resp.StatusCode, $resp.Headers['Content-Type'], $resp.Headers['Content-Encoding'], $resp.Headers['Content-Length'], $n, $resp.Headers['Location']
  $resp.Close()
}
