<#
Local Android emulator for APK checks on the Windows PC: one AVD, host-GPU window, adb helpers, and a one-call scenario.
Works for any game: nothing here knows the package. Keep this file ASCII-only (Windows PowerShell 5.1).

  setup                      install system image + create the AVD (once; downloads ~1.5 GB)
  boot [headless]            start the emulator and wait for Android. Default = a real window in the interactive session with the
                             PC GPU (realistic FPS). headless = no window, software graphics: only for smoke checks, FPS is meaningless
  session <apk> <package> [fresh] [-Wait 40]
                             ONE call: install -r (+pm clear when "fresh"), launch, wait, screenshot, dump logcat tag MC
                             -> <cwd>\.cache\avd\session.png and session-mc.txt (fetch both with on-remote --fetch)
  install <apk> | launch <package> | shot <name> | logs <tag> <name> | adb <args...> | tap <x> <y> | kill

Before every emulator start/restart, ask the user and obtain explicit consent for that launch.
Publishing, QA, headless mode and retries do not grant permission; see references/emulator.md.
Run from the project root on the PC:  on-remote powershell -NoProfile -ExecutionPolicy Bypass -File <skill>\scripts\avd_local.ps1 <action> ...
Every on-remote call pays for the tunnel: use "session", do not chain many small calls.
#>
param([string]$Action, [Parameter(ValueFromRemainingArguments)][string[]]$Rest)
$ErrorActionPreference = 'Continue'
$sdk = "$env:LOCALAPPDATA\Android\Sdk"; $env:ANDROID_SDK_ROOT = $sdk; $env:ANDROID_HOME = $sdk
$avdHome = "$env:USERPROFILE\.android\avd"; $env:ANDROID_AVD_HOME = $avdHome
$name = 'mc-a34'; $port = 5570; $serial = "emulator-$port"
$image = 'system-images;android-34;google_apis;x86_64'
# reuse an AVD that already exists (any name) instead of creating a second one
if (-not (Test-Path "$avdHome\$name.ini")) { $have = Get-ChildItem $avdHome -Filter *.ini -ErrorAction SilentlyContinue | Select-Object -First 1; if ($have) { $name = $have.BaseName } }
$adb = "$sdk\platform-tools\adb.exe"
$out = Join-Path (Get-Location) '.cache\avd'
$u = Get-ChildItem 'C:\Program Files\Unity\Hub\Editor\*\Editor\Data\PlaybackEngines\AndroidPlayer' -Directory -ErrorAction SilentlyContinue | Sort-Object FullName | Select-Object -Last 1
if ($u) { $env:JAVA_HOME = "$($u.FullName)\OpenJDK" }

function Get-Bin {
  # avdmanager resolves the SDK from its own location, so run a copy that lives inside the local SDK.
  $dst = "$sdk\cmdline-tools\latest\bin"
  if (-not (Test-Path "$dst\avdmanager.bat")) {
    $src = Get-ChildItem "$($u.FullName)\SDK\cmdline-tools" -Directory | Sort-Object Name | Select-Object -Last 1
    New-Item "$sdk\cmdline-tools" -ItemType Directory -Force | Out-Null
    Copy-Item $src.FullName "$sdk\cmdline-tools\latest" -Recurse -Force
  }
  $dst
}
function Wait-Boot([int]$limit) {
  $t = [Diagnostics.Stopwatch]::StartNew()
  while ((& $adb -s $serial shell getprop sys.boot_completed 2>$null) -ne '1') {
    if ($t.Elapsed.TotalSeconds -gt $limit) { return $false }
    Start-Sleep 3
  }
  return $true
}
function Is-Up { [bool]((& $adb devices 2>$null) -match $serial) }

switch ($Action) {
  'setup' {
    $bin = Get-Bin
    'y' * 20 | & "$bin\sdkmanager.bat" "--sdk_root=$sdk" --licenses | Out-Null
    & "$bin\sdkmanager.bat" "--sdk_root=$sdk" $image
    if ($LASTEXITCODE) { throw "sdkmanager exit $LASTEXITCODE" }
    New-Item $avdHome -ItemType Directory -Force | Out-Null
    if (-not (Test-Path "$avdHome\$name.ini")) {
      'no' | & "$bin\avdmanager.bat" -s create avd -n $name -k $image -d pixel_6 --force
      if ($LASTEXITCODE) { throw "avdmanager exit $LASTEXITCODE" }
    }
    Get-ChildItem $avdHome -Filter *.ini -Name
  }
  'boot' {
    if ((Is-Up) -and (& $adb -s $serial shell getprop sys.boot_completed 2>$null) -eq '1') { "$serial already up"; return }
    $headless = $Rest -contains 'headless'
    # unique log per boot: a log still held open by a previous emulator makes the redirect (and the whole task) fail with exit 1
    Get-ChildItem $avdHome -Filter "$name-*.log" -ErrorAction SilentlyContinue | ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }
    $log = "$avdHome\$name-" + (Get-Date -Format HHmmss) + ".log"
    $flags = "-avd $name -port $port -no-audio -no-boot-anim -no-snapshot " + $(if ($headless) { '-no-window -gpu swiftshader_indirect' } else { '-gpu host -no-metrics' })
    $cmdline = "set ANDROID_SDK_ROOT=$sdk&& set ANDROID_AVD_HOME=$avdHome&& `"$sdk\emulator\emulator.exe`" $flags > `"$log`" 2>&1"
    if ($headless) {
      $r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = "cmd /c $cmdline"; CurrentDirectory = $sdk }
      if ($r.ReturnValue) { throw "WMI Create -> $($r.ReturnValue)" }
    } else {
      # A scheduled task in the interactive session gives the emulator a real window and GPU access (WMI/ssh sessions have none).
      $task = 'McEmulatorWindow'
      Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction SilentlyContinue
      $taskAction = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument "/c $cmdline"
      $principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
      Register-ScheduledTask -TaskName $task -Action $taskAction -Principal $principal -Settings (New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Hours 4)) | Out-Null
      Start-ScheduledTask -TaskName $task
    }
    if (-not (Wait-Boot 240)) { Get-Content $log -ErrorAction SilentlyContinue | Where-Object { $_ -notmatch 'IPv6' } | Select-Object -Last 12; throw "$name did not boot in 4 min" }
    "$name ($serial) booted"
  }
  'install' { & $adb -s $serial install -r -d $Rest[0] }
  'launch' { & $adb -s $serial shell monkey -p $Rest[0] -c android.intent.category.LAUNCHER 1 }
  'tap' { & $adb -s $serial shell input tap $Rest[0] $Rest[1] }
  'adb' { & $adb -s $serial @Rest }
  'shot' {
    New-Item $out -ItemType Directory -Force | Out-Null
    & $adb -s $serial shell screencap -p /sdcard/mc_shot.png
    & $adb -s $serial pull /sdcard/mc_shot.png "$out\$($Rest[0]).png" | Out-Null
    "$out\$($Rest[0]).png"
  }
  'logs' {
    # redirect on the device, then pull: piping logcat through the tunnel is slow and truncates
    New-Item $out -ItemType Directory -Force | Out-Null
    & $adb -s $serial shell "logcat -d -s $($Rest[0]) > /sdcard/mc_logs.txt"
    & $adb -s $serial pull /sdcard/mc_logs.txt "$out\$($Rest[1]).txt" | Out-Null
    "$out\$($Rest[1]).txt"
  }
  'session' {
    $apk = $Rest[0]; $pkg = $Rest[1]
    $wait = 40; $i = [array]::IndexOf($Rest, '-Wait'); if ($i -ge 0) { $wait = [int]$Rest[$i + 1] }
    if (-not (Is-Up)) { throw 'emulator is not running: boot first' }
    & $adb -s $serial install -r -d $apk
    if ($Rest -contains 'fresh') { & $adb -s $serial shell pm clear $pkg | Out-Null }
    & $adb -s $serial logcat -c
    & $adb -s $serial shell monkey -p $pkg -c android.intent.category.LAUNCHER 1 | Out-Null
    Start-Sleep $wait
    New-Item $out -ItemType Directory -Force | Out-Null
    & $adb -s $serial shell screencap -p /sdcard/mc_shot.png
    & $adb -s $serial pull /sdcard/mc_shot.png "$out\session.png" | Out-Null
    & $adb -s $serial shell "logcat -d -s MC AndroidRuntime > /sdcard/mc_logs.txt"
    & $adb -s $serial pull /sdcard/mc_logs.txt "$out\session-mc.txt" | Out-Null
    "pid=" + (& $adb -s $serial shell pidof $pkg)
    "$out\session.png"; "$out\session-mc.txt"
  }
  'kill' {
    & $adb -s $serial emu kill 2>$null
    Unregister-ScheduledTask -TaskName 'McEmulatorWindow' -Confirm:$false -ErrorAction SilentlyContinue
    Get-CimInstance Win32_Process | Where-Object { $_.Name -like 'qemu-system*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
    'stopped'
  }
  default { 'actions: setup | boot [headless] | session <apk> <package> [fresh] [-Wait N] | install | launch | tap | shot | logs | adb | kill' }
}
