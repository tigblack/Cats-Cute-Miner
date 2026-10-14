<#
  Быстрая проверка компиляции Unity-кода без запуска Unity (секунды вместо минут):
  CatMiners.Runtime, DevTools, Editor и PlayMode-тесты собираются dotnet build под netstandard2.1 / C# 9
  со ссылками на модули UnityEngine/UnityEditor из установки Unity и на сборки пакетов из Library копии build\ci.
  Это не замена компиляции в Unity (tools/unity.ps1 -Mode Compile), а быстрый фильтр ошибок.
  Запуск: powershell -ExecutionPolicy Bypass -File Tools/UnityCheck/run.ps1 [-Instance <имя>] [-Project Runtime|DevTools|Editor|PlayModeTests|All]
#>
param([string]$Instance = '', [ValidateSet('All', 'Runtime', 'DevTools', 'Editor', 'PlayModeTests')][string]$Project = 'All')
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path -Parent (Split-Path -Parent $here)
$managed = 'C:\Unity\Editor\Data\Managed'
if ($env:UNITY_EXE) { $managed = Join-Path (Split-Path -Parent $env:UNITY_EXE) 'Data\Managed' }
$scripts = Join-Path $root 'build\ci\Library\ScriptAssemblies'
$cache = Join-Path $root 'build\ci\Library\PackageCache'
if (-not (Test-Path $scripts)) { throw "Нет ${scripts}: сначала выполните Tools/unity.ps1 -Mode Compile (создаёт копию build\ci)." }
$nunit = Get-ChildItem $cache -Recurse -Filter 'nunit.framework.dll' | Where-Object { $_.FullName -like '*unity-custom*' } | Select-Object -First 1 -ExpandProperty FullName
$newtonsoft = Get-ChildItem $cache -Recurse -Filter 'Newtonsoft.Json.dll' | Where-Object { $_.FullName -notlike '*AOT*' } | Select-Object -First 1 -ExpandProperty FullName

function Sync-Template([string]$template, [string]$target) {
    $new = [System.IO.File]::ReadAllText($template)
    if (-not (Test-Path $target) -or [System.IO.File]::ReadAllText($target) -ne $new) { [System.IO.File]::WriteAllText($target, $new) }
}
Sync-Template (Join-Path $root 'Tools\CoreTests\CoreLib\CoreLib.csproj.template') (Join-Path $root 'Tools\CoreTests\CoreLib\CoreLib.csproj')
$names = @('Runtime', 'DevTools', 'Editor', 'PlayModeTests')
foreach ($n in $names) { Sync-Template (Join-Path $here "$n\$n.csproj.template") (Join-Path $here "$n\$n.csproj") }
$targets = if ($Project -eq 'All') { $names } else { @($Project) }
$inst = if ($Instance) { $Instance } else { 'default' }
$props = @("-p:UnityManaged=$managed", "-p:ScriptAssemblies=$scripts", "-p:NUnitDll=$nunit", "-p:NewtonsoftDll=$newtonsoft",
           "-p:UnityCheckInstance=$inst", "-p:CoreTestsInstance=uc-$inst")
$failed = 0
foreach ($t in $targets) {
    $out = & dotnet build (Join-Path $here "$t\$t.csproj") --nologo -v q -clp:NoSummary @props 2>&1
    $errors = $out | Where-Object { $_ -match 'error (CS|MSB)\d+' } | ForEach-Object { ($_ -replace '\s*\[[^\]]*\.csproj\]\s*$', '') } | Select-Object -Unique
    if ($LASTEXITCODE -ne 0 -or $errors) {
        $failed++
        Write-Host "[UnityCheck] ${t}: ОШИБКИ"
        $errors | ForEach-Object { Write-Host "  $_" }
        if (-not $errors) { $out | Select-Object -Last 15 | ForEach-Object { Write-Host "  $_" } }
    } else { Write-Host "[UnityCheck] ${t}: OK" }
}
exit $failed
