[CmdletBinding()]
param([ValidateSet('Prepare', 'Push', 'VerifyClone')][string]$Mode = 'Prepare')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$repository = 'tigblack/Cats-Cute-Miner'
$url = "https://github.com/$repository.git"
$gh = Join-Path $root 'Library\WebPublishing\cli\bin\gh.exe'
if (-not (Test-Path -LiteralPath $gh)) { throw 'Existing private GitHub CLI installation unavailable.' }
$env:GH_CONFIG_DIR = Join-Path $root 'Library\WebPublishing\auth'
$env:PATH = (Split-Path -Parent $gh) + [IO.Path]::PathSeparator + $env:PATH
function GhJson([string[]]$arguments) {
    $output = & $gh @arguments
    if ($LASTEXITCODE -ne 0) { throw 'GitHub request failed; no credentials are included in this report.' }
    ($output | Out-String | ConvertFrom-Json)
}
function GitRun([string[]]$arguments) {
    & git -c 'credential.helper=' -c 'credential.helper=!gh auth git-credential' @arguments
    if ($LASTEXITCODE -ne 0) { throw ('Git command failed: ' + $arguments[0]) }
}
function AssertSources {
    foreach ($path in @('Assets\Game\Scenes\CoreGame.unity','Packages\manifest.json','Packages\packages-lock.json','ProjectSettings\ProjectVersion.txt','.gitignore','.gitattributes','README.md')) {
        if (-not (Test-Path -LiteralPath (Join-Path $root $path))) { throw "Required source missing: $path" }
    }
    $binaryHashes = Get-Content -LiteralPath (Join-Path $root 'Tools\ReleaseSdk\meta-binary-hashes.json') -Raw | ConvertFrom-Json
    # Validate the exact binaries installed from the official Meta package on this PC.
    foreach ($property in $binaryHashes.files.PSObject.Properties) {
        $path = Join-Path $root $property.Name
        if (-not (Test-Path -LiteralPath $path)) { throw "Bundled Meta SDK binary missing: $($property.Name)" }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne ([string]$property.Value).ToLowerInvariant()) {
            throw "Bundled Meta SDK binary hash mismatch: $($property.Name)"
        }
    }
}
function AuditIndex {
    $files = @(& git -c core.quotepath=false ls-files)
    if ($LASTEXITCODE -ne 0 -or -not $files.Count) { throw 'Source index is empty or unreadable.' }
    $blocked = @($files | Where-Object {
        $_ -match '(?i)(^|/)(Library|Temp|Backups|build|builds|obj|bin|node_modules|\.t3|\.claude|\.codex|UserSettings)(/|$)' -or
        $_ -match '(?i)(\.local\.(json|md|env)$|\.secret\.json$|\.(keystore|jks|p12|pfx|pem|key)$|(^|/)\.env(?:\.|$))' -or
        $_ -match '^Docs/Reports/'
    })
    if ($blocked.Count) { throw ('Excluded/private path staged: ' + ($blocked -join ', ')) }
    $patterns = @(
        '(?<![A-Za-z0-9_])gh[pousr]_[A-Za-z0-9]{30,}(?![A-Za-z0-9_])',
        '(?<![A-Za-z0-9_])github_pat_[A-Za-z0-9_]{60,}(?![A-Za-z0-9_])',
        '(?<![A-Za-z0-9_])AKIA[0-9A-Z]{16}(?![A-Za-z0-9_])',
        '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',
        '"private_key"\s*:\s*"-----BEGIN'
    )
    $suspect = @()
    $totalBytes = 0L
    $largest = @()
    foreach ($file in $files) {
        $item = Get-Item -LiteralPath (Join-Path $root $file)
        $totalBytes += $item.Length
        if ($item.Length -gt 10MB) { $largest += [pscustomobject]@{path=$file;bytes=$item.Length} }
        if ($file -match '\.(cs|ps1|py|js|cjs|json|md|xml|yml|yaml|txt|gradle|properties|config|sh)$' -and $item.Length -lt 20MB) {
            $content = [IO.File]::ReadAllText($item.FullName)
            foreach ($pattern in $patterns) { if ($content -cmatch $pattern) { $suspect += $file; break } }
        }
    }
    if ($suspect.Count) { throw ('Possible management credential/private key in staged text; inspect paths only: ' + ($suspect -join ', ')) }
    [pscustomobject]@{ trackedFiles=$files.Count; totalSourceBytes=$totalBytes; largestFiles=@($largest | Sort-Object bytes -Descending | Select-Object -First 12); excludedPrivatePaths=0; credentialPatternMatches=0 }
}
$user = GhJson @('api','user')
if ($user.login -ne 'tigblack') { throw 'The authenticated GitHub owner differs from the intended own repository.' }
Push-Location $root
try {
    if ($Mode -eq 'Prepare') {
        AssertSources
        if (-not (Test-Path -LiteralPath '.git')) { GitRun @('init','-b','main') }
        $remotes = @(& git remote)
        if ($remotes.Count) { throw 'Preserving an existing remote; inspect before preparing this new source repository.' }
        GitRun @('config','--local','user.name',$user.login)
        GitRun @('config','--local','user.email',"$($user.id)+$($user.login)@users.noreply.github.com")
        GitRun @('config','--local','core.autocrlf','false')
        GitRun @('lfs','install','--local')
        GitRun @('add','--','.gitignore','.gitattributes','README.md','Assets','Packages','ProjectSettings','Docs','Tools','google-play-publish','release')
        $audit = AuditIndex
        New-Item -ItemType Directory -Force -Path 'Docs\Reports' | Out-Null
        $audit | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath 'Docs\Reports\git-source-preflight.json' -Encoding UTF8
        $audit | ConvertTo-Json -Depth 6
    }
    elseif ($Mode -eq 'Push') {
        if (-not (Test-Path -LiteralPath '.git')) { throw 'Prepare the source index first.' }
        AssertSources
        $null = AuditIndex
        # Read own resources before creating; never change visibility of an existing public repository.
        $repos = GhJson @('api','user/repos?per_page=100&affiliation=owner&sort=full_name')
        $repo = @($repos | Where-Object { $_.full_name -eq $repository })
        if ($repo.Count -gt 1) { throw 'Ambiguous repository discovery.' }
        if (-not $repo.Count) {
            & $gh repo create $repository --private --description 'Cats Cute Miner Unity project, development documents and Google Play preparation'
            if ($LASTEXITCODE -ne 0) { throw 'Repository create did not succeed; reconcile before retrying.' }
        }
        $verified = GhJson @('api',"repos/$repository")
        if (-not $verified.private -or -not $verified.permissions.push) { throw 'Own repository is not private/writable; do not push sources.' }
        if ($verified.size -gt 0 -and -not (& git remote)) { throw 'Existing repository contains data; preserve it and inspect before setting origin.' }
        $remotes = @(& git remote)
        if ($remotes -notcontains 'origin') { GitRun @('remote','add','origin',$url) }
        elseif ((& git remote get-url origin) -ne $url) { throw 'Existing origin differs from the accepted source repository.' }
        & git diff --cached --quiet
        if ($LASTEXITCODE -eq 1) { GitRun @('commit','-m','Import Cats Cute Miner Unity project and release preparation') }
        elseif ($LASTEXITCODE -ne 0) { throw 'Cannot inspect staged source changes.' }
        GitRun @('lfs','fsck')
        GitRun @('push','--set-upstream','origin','main')
        $commit = (& git rev-parse HEAD).Trim()
        $remoteCommit = ((GitRun @('ls-remote','origin','refs/heads/main')) -split '\s+')[0]
        if ($commit -ne $remoteCommit) { throw 'Remote commit readback differs from local commit.' }
        [pscustomobject]@{ repository=$verified.full_name; private=$verified.private; url=$verified.html_url; commit=$commit; branch='main'; remoteCommitVerified=$true } | ConvertTo-Json
    }
    else {
        $commit = (& git rev-parse HEAD).Trim()
        $destination = Join-Path $root ('Temp\GitCloneVerification-' + $commit.Substring(0,12))
        if (Test-Path -LiteralPath $destination) { throw 'Clone verification folder already exists; preserve it and use its prior result.' }
        GitRun @('clone','--depth','1',$url,$destination)
        Push-Location $destination
        try {
            GitRun @('lfs','pull')
            GitRun @('lfs','fsck')
            $cloneCommit = (& git rev-parse HEAD).Trim()
            if ($cloneCommit -ne $commit) { throw 'Fresh clone differs from the pushed source commit.' }
            $original = $root
            $assetFiles = @(GitRun @('lfs','ls-files','--name-only') | Where-Object { $_ -like 'Assets/*' })
            foreach ($file in $assetFiles) {
                if ((Get-FileHash -LiteralPath (Join-Path $original $file)).Hash -ne (Get-FileHash -LiteralPath $file).Hash) {
                    throw "Cloned Unity resource hash mismatch: $file"
                }
            }
            [pscustomobject]@{ repository=$repository; commit=$commit; freshCloneVerified=$true; verifiedUnityBinaryAssetCount=$assetFiles.Count; unityBinaryAssetHashesMatch=$true; gitLfsVerified=$true; macUnityImport='not_tested_on_mac' } | ConvertTo-Json
        } finally { Pop-Location }
    }
} finally { Pop-Location }
