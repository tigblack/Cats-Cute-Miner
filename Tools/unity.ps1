<#
.SYNOPSIS
  Запуск Unity без GUI для проекта Cats Cute Miner: компиляция, тесты, валидатор, настройка, сборка APK.

.DESCRIPTION
  Target:
    Real — сам проект (нельзя, пока проект открыт в Editor);
    Ci   — изолированная копия build\ci (Assets/Packages/ProjectSettings зеркалятся из проекта, Library своя);
    Auto — Real, если проект не открыт в Editor, иначе Ci.
  В копии Ci для PlayMode и BuildApk сначала выполняется Setup (зеркалирование стирает созданные им файлы).
  В Real то же самое, если проект ещё не настроен (нет ProjectSettings\CatMinersSetup.json или сцены CoreGame).
  Логи и результаты пишутся в Docs\Reports (синхронизируются на хаб).

  Критерии (ТЗ 17.3, 19 п. 6):
    Compile  — успех только при коде 0 и без строк «error CS» в логе; лог копируется в Docs\Reports\compile.log.
    BuildPlayable — playable-сборка WebGL (BuildWeb.Playable, флаг CATMINERS_PLAYABLE) в <проект>\Builds\Playable,
               отчёт Docs\Reports\build-playable.json; Builds\Web и build-web.json основной игры не трогаются.
    BuildApk — до запуска Unity проверяется модуль Android Build Support (PlaybackEngines\AndroidPlayer и его SDK,
               NDK, OpenJDK); если чего-то нет — Docs\Reports\build-android.json с result = ModuleMissing и код 3.
               Собираются Development APK и APK без инструментов в <проект>\Builds\Android (и для копии Ci);
               строки missing script в логе сборки дают ненулевой код (4).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File Tools/unity.ps1 -Mode EditMode
#>
param(
    [ValidateSet('Compile', 'EditMode', 'PlayMode', 'Validate', 'Setup', 'BuildApk', 'BuildWeb', 'BuildPlayable')]
    [string]$Mode = 'Compile',
    [ValidateSet('Auto', 'Real', 'Ci')]
    [string]$Target = 'Auto',
    [string]$UnityExe = $env:UNITY_EXE,
    [string]$TestFilter = '',
    [ValidateSet('', 'Android', 'StandaloneWindows64', 'WebGL')]
    [string]$BuildTarget = '',
    [switch]$NoSync
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Reports = Join-Path $Root 'Docs\Reports'
$ApkOutput = Join-Path $Root 'Builds\Android'
$WebOutput = Join-Path $Root 'Builds\Web'
$PlayableOutput = Join-Path $Root 'Builds\Playable'
New-Item -ItemType Directory -Force -Path $Reports | Out-Null

function Find-Unity {
    if ($UnityExe -and (Test-Path $UnityExe)) { return $UnityExe }
    $versionLine = Get-Content (Join-Path $Root 'ProjectSettings\ProjectVersion.txt') | Where-Object { $_ -match '^m_EditorVersion:' } | Select-Object -First 1
    $version = ($versionLine -split ':', 2)[1].Trim()
    $candidates = @(
        "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe",
        "C:\Unity\$version\Editor\Unity.exe",
        "C:\Unity\Editor\Unity.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) {
            $pv = (Get-Item $c).VersionInfo.ProductVersion
            if ($pv -like "$version*") { return $c }
        }
    }
    throw "Unity $version не найден. Укажите путь в переменной UNITY_EXE."
}

function Test-ProjectLocked([string]$projectPath) {
    $lock = Join-Path $projectPath 'Temp\UnityLockfile'
    if (-not (Test-Path $lock)) { return $false }
    try {
        $s = [System.IO.File]::Open($lock, 'Open', 'ReadWrite', 'None')
        $s.Close()
        return $false
    } catch {
        return $true
    }
}

function Sync-CiMirror([string]$ciPath) {
    New-Item -ItemType Directory -Force -Path $ciPath | Out-Null
    foreach ($dir in @('Assets', 'Packages', 'ProjectSettings')) {
        $src = Join-Path $Root $dir
        $dst = Join-Path $ciPath $dir
        $null = robocopy $src $dst /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1
        if ($LASTEXITCODE -ge 8) { throw "robocopy $dir failed with code $LASTEXITCODE" }
    }
    $global:LASTEXITCODE = 0
}

function Write-JsonReport([string]$file, $data) {
    $json = $data | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText((Join-Path $Reports $file), $json + "`n", (New-Object System.Text.UTF8Encoding $false))
}

# Пути External Tools из настроек Unity (EditorPrefs в реестре: REG_BINARY со строкой UTF-8).
function Get-UnityEditorPref([string]$pattern) {
    $key = 'HKCU:\Software\Unity Technologies\Unity Editor 5.x'
    if (-not (Test-Path $key)) { return }
    $item = Get-Item $key
    foreach ($name in $item.Property) {
        if ($name -notmatch $pattern) { continue }
        $v = $item.GetValue($name)
        if ($v -is [byte[]]) { $v = [System.Text.Encoding]::UTF8.GetString($v).TrimEnd([char]0) }
        if ($v) { [string]$v }
    }
}

# Недостающие части Android Build Support (ТЗ 19 п. 6: «указать, какого именно — SDK, NDK, OpenJDK»).
# Встроенный каталог модуля или внешний путь из Preferences → External Tools.
function Get-MissingAndroidModules([string]$unity) {
    $player = Join-Path (Split-Path -Parent $unity) 'Data\PlaybackEngines\AndroidPlayer'
    if (-not (Test-Path -LiteralPath $player)) { return @('AndroidPlayer') }
    $missing = @()
    $tools = @(
        @{ Name = 'SDK'; Pref = '^AndroidSdkRoot' },
        @{ Name = 'NDK'; Pref = '^AndroidNdkRoot' },
        @{ Name = 'OpenJDK'; Pref = '^JdkPath' }
    )
    foreach ($t in $tools) {
        if (Test-Path -LiteralPath (Join-Path $player $t.Name)) { continue }
        $external = Get-UnityEditorPref $t.Pref | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
        if (-not $external) { $missing += $t.Name }
    }
    return $missing
}

function Test-ProjectSetUp([string]$projectPath) {
    (Test-Path -LiteralPath (Join-Path $projectPath 'ProjectSettings\CatMinersSetup.json')) -and
    (Test-Path -LiteralPath (Join-Path $projectPath 'Assets\Game\Scenes\CoreGame.unity'))
}

# Setup; если он переключил activeInputHandler и просит перезапуск — второй запуск.
function Invoke-Setup([string]$unity, [string]$projectPath) {
    $code = Invoke-Unity $unity $projectPath 'Setup'
    if ($code -ne 0) {
        $log = Join-Path $Reports 'unity-setup.log'
        if ((Test-Path $log) -and (Select-String -Path $log -Pattern 'activeInputHandler' -SimpleMatch -Quiet)) {
            Write-Host '[unity.ps1] Setup переключил обработчик ввода: повторный запуск Setup'
            $code = Invoke-Unity $unity $projectPath 'Setup'
        }
    }
    return $code
}

function Invoke-Unity([string]$unity, [string]$projectPath, [string]$mode) {
    $log = Join-Path $Reports ("unity-{0}.log" -f $mode.ToLower())
    $unityArgs = @('-batchmode', '-projectPath', "`"$projectPath`"", '-logFile', "`"$log`"", '-catminersReports', "`"$Reports`"")
    switch ($mode) {
        'Compile' { $unityArgs += @('-nographics', '-quit') }
        'EditMode' {
            $xml = Join-Path $Reports 'editmode-results.xml'
            if (Test-Path $xml) { Remove-Item $xml }
            $unityArgs += @('-nographics', '-runTests', '-testPlatform', 'EditMode', '-testResults', "`"$xml`"")
            if ($TestFilter) { $unityArgs += @('-testFilter', "`"$TestFilter`"") }
        }
        'PlayMode' {
            $xml = Join-Path $Reports 'playmode-results.xml'
            if (Test-Path $xml) { Remove-Item $xml }
            $unityArgs += @('-runTests', '-testPlatform', 'PlayMode', '-testResults', "`"$xml`"")
            if ($TestFilter) { $unityArgs += @('-testFilter', "`"$TestFilter`"") }
        }
        'Validate' { $unityArgs += @('-nographics', '-quit', '-executeMethod', 'CatMiners.Editor.OutpostValidatorCli.Run') }
        'Setup' { $unityArgs += @('-quit', '-executeMethod', 'CatMiners.Editor.ProjectSetup.RunBatch') }
        'BuildApk' { $unityArgs += @('-quit', '-buildTarget', 'Android', '-executeMethod', 'CatMiners.Editor.BuildAndroid.Apk', '-catminersOutput', "`"$ApkOutput`"") }
        'BuildWeb' { $unityArgs += @('-quit', '-buildTarget', 'WebGL', '-executeMethod', 'CatMiners.Editor.BuildWeb.Pages', '-catminersOutput', "`"$WebOutput`"") }
        'BuildPlayable' { $unityArgs += @('-quit', '-buildTarget', 'WebGL', '-executeMethod', 'CatMiners.Editor.BuildWeb.Playable', '-catminersOutput', "`"$PlayableOutput`"") }
    }
    # Активная платформа редактора (например, Android для замера с Quality = Mobile).
    if ($BuildTarget -and $mode -notin @('BuildApk', 'BuildWeb', 'BuildPlayable')) { $unityArgs += @('-buildTarget', $BuildTarget) }
    Write-Host ("[unity.ps1] mode={0} project={1}" -f $mode, $projectPath)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process -FilePath $unity -ArgumentList $unityArgs -NoNewWindow -PassThru -Wait
    $code = $proc.ExitCode
    $sw.Stop()
    Write-Host ("[unity.ps1] {0}: exit={1} time={2:n0}s log={3}" -f $mode, $code, $sw.Elapsed.TotalSeconds, $log)
    $errors = $null
    if (Test-Path $log) {
        $errors = Select-String -Path $log -Pattern 'error CS\d+' | Select-Object -ExpandProperty Line -Unique
        if ($errors) {
            Write-Host '[unity.ps1] compile errors:'
            $errors | Select-Object -First 60 | ForEach-Object { Write-Host ("  " + $_.Trim()) }
        }
        $exceptions = Select-String -Path $log -Pattern '(Exception|Error):' -CaseSensitive |
            Where-Object { $_.Line -notmatch 'error CS|Licensing|Rider|pipe is being closed' } |
            Select-Object -ExpandProperty Line -Unique
        if ($exceptions) {
            Write-Host '[unity.ps1] errors/exceptions in log (first 25):'
            $exceptions | Select-Object -First 25 | ForEach-Object { Write-Host ("  " + $_.Trim()) }
        }
    }
    if ($mode -in @('EditMode', 'PlayMode')) {
        $xml = Join-Path $Reports ("{0}-results.xml" -f $mode.ToLower())
        if (Test-Path $xml) {
            [xml]$doc = Get-Content $xml -Encoding UTF8
            $run = $doc.'test-run'
            Write-Host ("[unity.ps1] tests total={0} passed={1} failed={2} skipped={3}" -f $run.total, $run.passed, $run.failed, $run.skipped)
            # Генераторы с TestFilter не должны заменять доказательство полного прогона.
            $archiveDir = Join-Path $Reports 'outpost'
            New-Item -ItemType Directory -Force -Path $archiveDir | Out-Null
            $scope = if ($TestFilter) { $TestFilter -replace '[^a-zA-Z0-9_.-]', '_' } else { 'all' }
            # Длинный фильтр давал имя архива длиннее 260 символов (PathTooLongException и код 1 при успешных тестах).
            if ($scope.Length -gt 60) {
                $sha = [System.Security.Cryptography.SHA1]::Create()
                $hash = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($TestFilter)))).Replace('-', '').Substring(0, 8)
                $scope = $scope.Substring(0, 50) + '_' + $hash
            }
            $archiveName = "{0}-{1}" -f $mode.ToLower(), $scope
            Copy-Item -LiteralPath $xml -Destination (Join-Path $archiveDir ($archiveName + '-results.xml')) -Force
            if (Test-Path $log) {
                Copy-Item -LiteralPath $log -Destination (Join-Path $archiveDir ('unity-' + $archiveName + '.log')) -Force
            }
            if (([int]$run.failed -gt 0 -or [int]$run.total -le 0) -and $code -eq 0) { $code = 1 }
            $failed = $doc.SelectNodes("//test-case[@result='Failed']")
            foreach ($f in $failed) {
                Write-Host ("  FAILED {0}" -f $f.fullname)
                $msg = $f.failure.message.'#cdata-section'
                if ($msg) { Write-Host ("    " + (($msg -split "`n" | Select-Object -First 8) -join "`n    ")) }
            }
        } else {
            Write-Host '[unity.ps1] no test results file produced'
            if ($code -eq 0) { $code = 1 }
        }
    }
    if ($mode -eq 'Compile') {
        # ТЗ 17.3: Compile успешен только при коде 0 и без «error CS»; доказательство — compile.log (ТЗ 18).
        # unity-compile.log остаётся: его читает генератор VALIDATION_REPORT.
        if (Test-Path $log) {
            Copy-Item -LiteralPath $log -Destination (Join-Path $Reports 'compile.log') -Force
            if ($errors -and $code -eq 0) {
                Write-Host '[unity.ps1] Compile: в логе есть строки error CS при коде 0 — провал (ТЗ 17.3)'
                $code = 1
            }
        } elseif ($code -eq 0) {
            Write-Host '[unity.ps1] Compile: лог не создан — проверить отсутствие error CS нельзя'
            $code = 1
        }
    }
    return $code
}

$unity = Find-Unity
$projectPath = $Root
$usedTarget = $Target
if ($Target -eq 'Auto') {
    $usedTarget = if (Test-ProjectLocked $Root) { 'Ci' } else { 'Real' }
}
if ($usedTarget -eq 'Real' -and (Test-ProjectLocked $Root)) {
    throw 'Проект открыт в Unity Editor. Закройте Editor или используйте -Target Ci.'
}
$code = 0
if ($Mode -in @('BuildWeb', 'BuildPlayable')) {
    $BuildTarget = 'WebGL'
    $webModule = Join-Path (Split-Path -Parent $unity) 'Data\PlaybackEngines\WebGLSupport'
    if (-not (Test-Path $webModule)) { throw 'Не установлен WebGL Build Support для этой версии Unity.' }
    # Playable пишет свой отчёт и свой каталог: build-web.json и Builds\Web основной игры не трогаются.
    $webReport = Join-Path $Reports $(if ($Mode -eq 'BuildPlayable') { 'build-playable.json' } else { 'build-web.json' })
    if (Test-Path $webReport) { Remove-Item $webReport }
}
$buildReport = Join-Path $Reports 'build-android.json'
if ($Mode -eq 'BuildApk') {
    # Модуль Android проверяется до запуска Unity: без него -buildTarget Android не стартует (ТЗ 19 п. 6).
    $missing = @(Get-MissingAndroidModules $unity)
    if ($missing.Count -gt 0) {
        $message = 'Нет модуля Android Build Support: ' + ($missing -join ', ') + '. Unity Hub → Installs → Add modules → Android Build Support (Android SDK & NDK Tools, OpenJDK).'
        Write-Host ('[unity.ps1] ' + $message)
        Write-JsonReport 'build-android.json' ([ordered]@{
            result       = 'ModuleMissing'
            missing      = $missing
            message      = $message
            source       = 'unity.ps1'
            editor       = $unity
            internalOnly = $true
            checkedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        })
        exit 3
    }
    if (Test-Path $buildReport) { Remove-Item $buildReport }
}
if ($usedTarget -eq 'Ci') {
    $projectPath = Join-Path $Root 'build\ci'
    if (-not $NoSync) { Sync-CiMirror $projectPath }
    if ($Mode -in @('PlayMode', 'BuildApk', 'BuildWeb', 'BuildPlayable')) {
        $code = Invoke-Setup $unity $projectPath
        if ($code -ne 0) { exit $code }
    }
} elseif ($Mode -in @('PlayMode', 'BuildApk', 'BuildWeb', 'BuildPlayable') -and -not (Test-ProjectSetUp $projectPath)) {
    # Настоящий проект ещё не настроен (нет маркера ProjectSetup или сцены CoreGame): AutoSetup в batchmode не работает.
    Write-Host '[unity.ps1] проект не настроен (нет ProjectSettings\CatMinersSetup.json или Assets\Game\Scenes\CoreGame.unity): сначала Setup'
    $code = Invoke-Setup $unity $projectPath
    if ($code -ne 0) { exit $code }
}
Write-Host ("[unity.ps1] target={0}" -f $usedTarget)
$code = Invoke-Unity $unity $projectPath $Mode
if ($Mode -in @('BuildWeb', 'BuildPlayable')) {
    $webLog = Join-Path $Reports ("unity-{0}.log" -f $Mode.ToLower())
    $badLines = @(Select-String -Path $webLog -Pattern 'error CS\d+|(?i)the referenced script|missing script|missing \(mono ?script\)' | Select-Object -ExpandProperty Line -Unique)
    if ($badLines.Count -gt 0) {
        $badLines | Select-Object -First 20 | ForEach-Object { Write-Host $_ }
        if ($code -eq 0) { $code = 4 }
    }
    if (-not (Test-Path $webReport)) {
        Write-Host ('[unity.ps1] {0} не создан' -f (Split-Path -Leaf $webReport))
        if ($code -eq 0) { $code = 1 }
    } else {
        $report = Get-Content $webReport -Raw -Encoding UTF8 | ConvertFrom-Json
        $label = if ($Mode -eq 'BuildPlayable') { 'Playable' } else { 'Web' }
        Write-Host ('[unity.ps1] {0}: result={1} bytes={2} path={3}' -f $label, $report.result, $report.sizeBytes, $report.path)
        if ($report.result -ne 'Succeeded' -and $code -eq 0) { $code = 1 }
    }
}
if ($Mode -eq 'BuildApk') {
    $log = Join-Path $Reports 'unity-buildapk.log'
    $missingScripts = @()
    if (Test-Path $log) {
        # ТЗ 5.2: «сборка без DEVELOPMENT_BUILD компилируется, в логе нет missing script».
        $missingScripts = @(Select-String -Path $log -Pattern '(?i)the referenced script|missing script|missing \(mono ?script\)|\bscript\b.*\bis missing' |
            Select-Object -ExpandProperty Line -Unique)
    }
    if ($missingScripts.Count -gt 0) {
        Write-Host '[unity.ps1] missing script в логе сборки:'
        $missingScripts | Select-Object -First 20 | ForEach-Object { Write-Host ('  ' + $_.Trim()) }
        if ($code -eq 0) { $code = 4 }
    }
    if (Test-Path $buildReport) {
        $report = Get-Content $buildReport -Raw -Encoding UTF8 | ConvertFrom-Json
        $report | Add-Member -NotePropertyName 'missingScriptLines' -NotePropertyValue $missingScripts -Force
        $report | Add-Member -NotePropertyName 'logCheck' -NotePropertyValue $(if ($missingScripts.Count -gt 0) { 'missing script в логе' } else { 'ok' }) -Force
        $report | Add-Member -NotePropertyName 'exitCode' -NotePropertyValue $code -Force
        Write-JsonReport 'build-android.json' $report
        Write-Host ('[unity.ps1] build-android.json: result={0}' -f $report.result)
        foreach ($b in @($report.builds)) {
            if ($b) { Write-Host ('  {0}: {1} {2} {3} bytes devTools={4}' -f $b.variant, $b.result, $b.path, $b.sizeBytes, $b.devToolsIncluded) }
        }
    } else {
        Write-Host '[unity.ps1] build-android.json не создан'
        Write-JsonReport 'build-android.json' ([ordered]@{
            result             = 'NoReport'
            exitCode           = $code
            missingScriptLines = $missingScripts
            log                = $log
            internalOnly       = $true
            checkedAtUtc       = (Get-Date).ToUniversalTime().ToString('o')
        })
        if ($code -eq 0) { $code = 1 }
    }
}
exit $code
