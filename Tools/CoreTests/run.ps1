<#
  Быстрая проверка CatMiners.Core без Unity: сборка под netstandard2.1 / C# 9 и прогон EditMode-тестов через dotnet test.
  Файлы *.csproj не синхронизируются Mutagen, поэтому они создаются здесь из шаблонов.
  Запуск: powershell -ExecutionPolicy Bypass -File Tools/CoreTests/run.ps1 [-Filter <фильтр dotnet test>] [-Instance <имя>]
  -Instance — свои obj/bin (obj/CoreTests-<имя>), чтобы несколько прогонов шли одновременно.
  -Draft <Имя.cs> — добавить черновик теста из Tools/CoreTests/Drafts (вне Assets, Unity его не видит).
#>
param([string]$Filter = '', [string]$Instance = '', [string]$Draft = '')
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
function Sync-Template([string]$template, [string]$target) {
    # Копируем только при отличии: параллельные прогоны не перезаписывают файл, который читает другой.
    $new = [System.IO.File]::ReadAllText($template)
    if (-not (Test-Path $target) -or [System.IO.File]::ReadAllText($target) -ne $new) {
        [System.IO.File]::WriteAllText($target, $new)
    }
}
Sync-Template (Join-Path $here 'CoreLib\CoreLib.csproj.template') (Join-Path $here 'CoreLib\CoreLib.csproj')
Sync-Template (Join-Path $here 'Tests\Tests.csproj.template') (Join-Path $here 'Tests\Tests.csproj')
$proj = Join-Path $here 'Tests\Tests.csproj'
$dotnetArgs = @('test', $proj, '--nologo', '-v', 'q', '--logger', 'console;verbosity=normal')
if ($Filter) { $dotnetArgs += @('--filter', $Filter) }
if ($Instance) { $dotnetArgs += @("-p:CoreTestsInstance=$Instance") }
if ($Draft) { $dotnetArgs += @("-p:CoreTestsDraft=$Draft") }
& dotnet @dotnetArgs 2>&1 | ForEach-Object { $_ } | Where-Object { $_ -notmatch '^\s*$' -and $_ -notmatch 'warning NU|Determining projects|Restored ' }
exit $LASTEXITCODE
