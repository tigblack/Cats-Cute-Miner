$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
foreach ($path in @('google-play-publish\assets\.env','google-play-publish\assets\.env.superleo','release\google-play\superleo.local.env','.env.superleo','release\google-play\access.local.json','google-play-publish\assets\.env.sdk.local')) {
    [ordered]@{ path=$path; exists=(Test-Path -LiteralPath (Join-Path $root $path)) } | ConvertTo-Json -Compress
}
