param(
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository = 'tigblack/Cat-Miners',
    # Optional commit message file (for example, with a Co-Authored-By trailer); default is a one-line message.
    [string]$MessageFile = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$web = Join-Path $root 'Builds\Web'
$checkout = Join-Path $root 'Builds\WebRepository'
$gh = Join-Path $root 'Library\WebPublishing\cli\bin\gh.exe'
$env:GH_CONFIG_DIR = Join-Path $root 'Library\WebPublishing\auth'
$env:PATH = (Split-Path -Parent $gh) + [IO.Path]::PathSeparator + $env:PATH
$reportPath = Join-Path $root 'Docs\Reports\build-web.json'
if (-not (Test-Path $reportPath)) { throw 'BuildWeb must finish successfully before publishing.' }
$build = Get-Content $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($build.result -ne 'Succeeded' -or -not (Test-Path (Join-Path $web 'index.html'))) { throw 'No successful web build.' }
if (-not (Test-Path $gh)) { throw 'GitHub CLI is not installed.' }
if ($MessageFile) {
    $MessageFile = (Resolve-Path $MessageFile).Path
    if (-not (Get-Content $MessageFile -Raw).Trim()) { throw 'The commit message file is empty.' }
}

function Gh-Json([string[]]$a) {
    $json = & $gh @a
    if ($LASTEXITCODE -ne 0) { throw ('GitHub command failed: ' + ($a -join ' ')) }
    return ($json | Out-String | ConvertFrom-Json)
}
function Git-Run([string[]]$a) {
    $helper = '!gh auth git-credential'
    & git -c 'credential.helper=' -c "credential.helper=$helper" @a
    if ($LASTEXITCODE -ne 0) { throw ('Git command failed: ' + ($a -join ' ')) }
}
$repo = Gh-Json @('api', "repos/$Repository")
$user = Gh-Json @('api', 'user')
if (-not $repo.permissions.push -or -not $repo.permissions.admin) { throw 'Repository write and administrator access are required for publishing and Pages settings.' }
foreach ($file in $build.files) {
    $source = Join-Path $web $file.name
    if (-not (Test-Path $source) -or (Get-Item $source).Length -ne $file.sizeBytes) { throw ('Build file is missing or changed: ' + $file.name) }
}
$parts = $Repository.Split('/')
$site = "https://$($parts[0].ToLower()).github.io/$($parts[1])/"

if (-not (Test-Path (Join-Path $checkout '.git'))) { Git-Run @('clone', "https://github.com/$Repository.git", $checkout) }
Push-Location $checkout
try {
    $remote = & git remote get-url origin
    if ($remote -ne "https://github.com/$Repository.git") { throw 'Publication checkout belongs to a different repository.' }
    $dirty = & git status --porcelain
    if ($dirty) { throw 'Publication checkout has pending changes; inspect them before retrying.' }
    Git-Run @('config', '--local', 'user.name', $user.login)
    Git-Run @('config', '--local', 'user.email', "$($user.id)+$($user.login)@users.noreply.github.com")
    Git-Run @('checkout', '-B', 'main')
    # Replace only generated web asset directories, preserving unrelated repository files.
    foreach ($name in @('Build', 'StreamingAssets', 'TemplateData')) {
        $old = Join-Path $checkout $name
        if (Test-Path $old) { Remove-Item $old -Recurse -Force }
    }
    Get-ChildItem $web -Force | ForEach-Object { Copy-Item $_.FullName -Destination $checkout -Recurse -Force }
    $readme = "# Cat Miners`n`n[Play in your browser]($site)`n`nTap or click stones to mine. The game saves progress in this browser automatically.`n`nUnity WebGL release build. GitHub Pages publishes the main branch from the repository root.`n"
    [IO.File]::WriteAllText((Join-Path $checkout 'README.md'), $readme, (New-Object Text.UTF8Encoding $false))
    Git-Run @('add', '--all')
    & git diff --cached --quiet
    if ($LASTEXITCODE -eq 1) {
        if ($MessageFile) { Git-Run @('commit', '-F', $MessageFile) } else { Git-Run @('commit', '-m', 'Publish Cat Miners WebGL build') }
    }
    elseif ($LASTEXITCODE -ne 0) { throw 'Could not inspect staged publication changes.' }
    Git-Run @('push', '--set-upstream', 'origin', 'main')
    $commit = & git rev-parse HEAD
    $pagesExists = [bool]$repo.has_pages
    $method = if ($pagesExists) { 'PUT' } else { 'POST' }
    & $gh api --method $method "repos/$Repository/pages" -f 'build_type=legacy' -f 'source[branch]=main' -f 'source[path]=/'
    if ($LASTEXITCODE -ne 0) { throw 'The build was pushed, but configuring GitHub Pages failed.' }
    & $gh api --method PATCH "repos/$Repository" -f "homepage=$site" --silent
    if ($LASTEXITCODE -ne 0) { throw 'The site was published, but updating the repository website link failed.' }
    $pages = Gh-Json @('api', "repos/$Repository/pages")
    $publication = [ordered]@{ repository=$Repository; commit=$commit; site=$pages.html_url; pagesStatus=$pages.status; source=$pages.source; sizeBytes=$build.sizeBytes; publishedAtUtc=(Get-Date).ToUniversalTime().ToString('o') }
    [IO.File]::WriteAllText((Join-Path $root 'Docs\Reports\github-pages-publication.json'), ($publication | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding $false))
    Write-Host "GitHub Pages: $($pages.html_url) ($($pages.status))"
} finally { Pop-Location }
