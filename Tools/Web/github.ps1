param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$gh = Join-Path $root 'Library\WebPublishing\cli\bin\gh.exe'
if (-not (Test-Path $gh)) { throw 'Portable GitHub CLI is not installed in Library/WebPublishing/cli/bin.' }
# Keep the authentication data in the private, unsynchronised Unity Library.
$env:GH_CONFIG_DIR = Join-Path $root 'Library\WebPublishing\auth'
& $gh @Arguments
exit $LASTEXITCODE
