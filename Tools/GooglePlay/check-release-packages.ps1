$ErrorActionPreference = 'Stop'
$paths = @(
    (Join-Path $env:USERPROFILE 'Downloads/CatMinerRelease/cat-miner-store-en-US.zip'),
    'Docs/Legal/CatsCuteMiner/cats-cute-miner-legal-review.zip',
    'Docs/Store/GooglePlay/assets/en-US/feature-1024x500.png',
    'release/advertising/applovin/CatMiner_AppLovin_Preparation.zip'
)
$results = foreach ($relativePath in $paths) {
    if (Test-Path -LiteralPath $relativePath -PathType Leaf) {
        $file = Get-Item -LiteralPath $relativePath
        [pscustomobject]@{path=$relativePath; bytes=$file.Length; sha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $relativePath).Hash.ToLowerInvariant()}
    } else {
        [pscustomobject]@{path=$relativePath; missing=$true}
    }
}
$identity = (Select-String -LiteralPath 'ProjectSettings/ProjectSettings.asset' -Pattern '^\s*productName:').Line.Trim()
[pscustomobject]@{productNameLine=$identity; files=@($results)} | ConvertTo-Json -Depth 5
