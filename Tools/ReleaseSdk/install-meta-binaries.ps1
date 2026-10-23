param([string]$ProjectPath = '.')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ProjectPath).Path
$expected = Get-Content (Join-Path $PSScriptRoot 'meta-binary-hashes.json') -Raw | ConvertFrom-Json
$stage = Join-Path $root ('Temp\meta-sdk-delivery-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    $zip = Join-Path $stage 'sdk.zip'
    Invoke-WebRequest -Uri $expected.source -OutFile $zip -UseBasicParsing -TimeoutSec 60
    if ((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected.zip_sha256) { throw 'Official Meta archive hash mismatch.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $unpacked = Join-Path $stage 'zip'
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $unpacked)
    $package = Get-ChildItem $unpacked -Recurse -Filter '*.unitypackage' | Select-Object -First 1
    if (-not $package) { throw 'Official archive has no Unity package.' }
    $tar = Join-Path $stage 'sdk.tar'
    $input = [IO.File]::OpenRead($package.FullName)
    $gzip = New-Object IO.Compression.GZipStream($input, [IO.Compression.CompressionMode]::Decompress)
    $output = [IO.File]::Create($tar)
    try { $gzip.CopyTo($output) } finally { $output.Dispose(); $gzip.Dispose(); $input.Dispose() }
    $assets = Join-Path $stage 'assets'
    New-Item -ItemType Directory -Path $assets | Out-Null
    & tar -xf $tar -C $assets
    if ($LASTEXITCODE -ne 0) { throw 'Failed to unpack official Unity archive.' }
    $count = 0
    foreach ($entry in Get-ChildItem $assets -Directory) {
        $pathname = Join-Path $entry.FullName 'pathname'
        $payload = Join-Path $entry.FullName 'asset'
        if (-not (Test-Path $pathname) -or -not (Test-Path $payload)) { continue }
        $path = (Get-Content -LiteralPath $pathname -Raw).Trim()
        $hashProperty = $expected.files.psobject.Properties[$path]
        if (-not $hashProperty) { continue }
        if (-not $path.StartsWith('Assets/FacebookSDK/') -or $path -match '(^|/)\.\.(/|$)') { throw 'Invalid SDK delivery path.' }
        $hash = (Get-FileHash $payload -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $hashProperty.Value) { throw ('SDK binary hash mismatch: ' + $path) }
        $target = Join-Path $root $path
        if (-not (Test-Path ($target + '.meta'))) { throw ('Synchronize SDK metadata first: ' + $path) }
        if ((Test-Path $target) -and (Get-FileHash $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw ('Existing different SDK binary: ' + $path) }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $payload -Destination $target -Force
        $count++
    }
    if ($count -ne @($expected.files.psobject.Properties).Count) { throw 'SDK delivery file count mismatch.' }
    [ordered]@{ source=$expected.source; version='18.1.1'; verified_binary_files=$count; status='delivered_to_build_machine'; runtime_ready=$false } | ConvertTo-Json
} finally { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
