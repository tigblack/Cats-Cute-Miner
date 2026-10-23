param([Parameter(Mandatory=$true)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedSha256)
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot '../../Docs/Store/GooglePlay/cat-miner-store-en-US.zip'
$actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash.ToLowerInvariant()
if ($actual -ne $ExpectedSha256.ToLowerInvariant()) { throw 'Source archive checksum mismatch' }
$folder = Join-Path $env:USERPROFILE 'Downloads/CatMinerRelease'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$destination = Join-Path $folder 'cat-miner-store-en-US.zip'
Copy-Item -LiteralPath $source -Destination $destination -Force
$copied = (Get-FileHash -Algorithm SHA256 -LiteralPath $destination).Hash.ToLowerInvariant()
if ($copied -ne $actual) { throw 'Copied archive checksum mismatch' }
[pscustomobject]@{path=$destination; sha256=$copied; bytes=(Get-Item -LiteralPath $destination).Length; verified=$true} | ConvertTo-Json -Compress
