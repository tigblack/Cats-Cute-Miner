param(
    [Parameter(Mandatory=$true)]
    [ValidatePattern('^[a-fA-F0-9]{64}$')]
    [string]$ExpectedSha256
)
$ErrorActionPreference = 'Stop'
$archive = 'release/advertising/applovin/CatMiner_AppLovin_Preparation.zip'
$destination = 'release/advertising/applovin'
$actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant()
if ($actual -ne $ExpectedSha256.ToLowerInvariant()) { throw 'Preparation archive checksum mismatch; not extracted' }
Expand-Archive -LiteralPath $archive -DestinationPath $destination -Force
[pscustomobject]@{expanded=$true; sha256=$actual; preview='release/advertising/applovin/index.html'; campaign_started=$false} | ConvertTo-Json -Compress
