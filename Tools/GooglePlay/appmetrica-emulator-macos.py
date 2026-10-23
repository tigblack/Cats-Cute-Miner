"""Project-local AppMetrica emulator QA. Setup never launches an emulator."""
import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
WORK = ROOT / 'build/private/appmetrica-emulator'
SDK = ROOT / 'build/private/appmetrica-android-sdk'
PLAYER = Path('/Applications/Unity/Hub/Editor/6000.6.4f1/PlaybackEngines/AndroidPlayer')
BUNDLED_CMDLINE = PLAYER / 'SDK/cmdline-tools/16.0'
CMDLINE = SDK / 'cmdline-tools/16.0/bin'
ADB = PLAYER / 'SDK/platform-tools/adb'
AVD_NAME = 'CatMiners_AppMetrica_35'
IMAGE = 'system-images;android-35;google_apis;arm64-v8a'
PORT = 5570
SERIAL = 'emulator-' + str(PORT)
APK = ROOT / 'build/private/appmetrica-smoke-0.1.4-5/cat-miners-0.1.4-5-from-aab.apk'
APK_SHA = 'b55ab637e18d23e3d8207667ff3d44af8d24b2e833eeca78f744bfea37f79675'

def now():
    return datetime.now(timezone.utc).isoformat()

def env():
    values = os.environ.copy()
    values.update(JAVA_HOME=str(PLAYER / 'OpenJDK'), ANDROID_HOME=str(SDK), ANDROID_SDK_ROOT=str(SDK),
                  ANDROID_USER_HOME=str(WORK / 'android-user'), ANDROID_EMULATOR_HOME=str(WORK / 'emulator-home'),
                  ANDROID_AVD_HOME=str(WORK / 'avd'))
    return values

def save(name, data):
    (WORK / name).write_text(json.dumps(data, indent=2) + '\n')

def report(stage, **data):
    value = dict(at=now(), stage=stage, **data)
    save('latest-stage.json', value)
    print(json.dumps(value), flush=True)

def capture(args, timeout=30, input=None):
    result = subprocess.run(list(map(str, args)), input=input, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            env=env(), timeout=timeout, check=False)
    if result.returncode:
        (WORK / 'last-command-error.log').write_bytes(result.stdout)
        raise RuntimeError('Command failed with exit code ' + str(result.returncode) + '; private diagnostic saved')
    return result.stdout.decode(errors='replace')

def adb(*args, timeout=30):
    return capture([ADB, '-s', SERIAL, *args], timeout=timeout)

def android_tool(name, *args):
    # Google's shell launchers split toolsdir when the Unity project path has spaces.
    # Invoke the same Java entry point with an argument array instead of shell eval.
    prop = 'com.android.sdklib.toolsdir' if name == 'sdkmanager' else 'com.android.sdkmanager.toolsdir'
    main = 'com.android.sdklib.tool.sdkmanager.SdkManagerCli' if name == 'sdkmanager' else 'com.android.sdklib.tool.AvdManagerCli'
    return [str(PLAYER / 'OpenJDK/bin/java'), '-D' + prop + '=' + str(CMDLINE.parent),
            '-classpath', str(CMDLINE.parent / 'lib' / (name + '-classpath.jar')), main, *map(str, args)]

def setup():
    for path in [SDK, WORK / 'android-user', WORK / 'emulator-home', WORK / 'avd']:
        path.mkdir(parents=True, exist_ok=True, mode=0o700)
    # avdmanager discovers its SDK from its own installation, not ANDROID_HOME.
    # Keep a project-local copy so it sees the isolated system-image repository.
    if not (CMDLINE / 'avdmanager').is_file():
        shutil.copytree(BUNDLED_CMDLINE, CMDLINE.parent, dirs_exist_ok=True)
    # The emulator rejects an SDK root without platform-tools, even if adb is
    # available from another SDK. Reuse Unity's verified existing tools locally.
    if not (SDK / 'platform-tools/adb').is_file():
        shutil.copytree(PLAYER / 'SDK/platform-tools', SDK / 'platform-tools', dirs_exist_ok=True)
    # Reuse the already accepted Unity Android SDK license receipts; no new global SDK edits.
    licenses = PLAYER / 'SDK/licenses'
    if licenses.is_dir():
        shutil.copytree(licenses, SDK / 'licenses', dirs_exist_ok=True)
    report('sdk_licenses_for_authorized_install', emulator_launched=False)
    with (WORK / 'sdk-licenses.log').open('wb') as log:
        accepted = subprocess.run(android_tool('sdkmanager', '--sdk_root=' + str(SDK), '--licenses'),
                                  input=b'y\n' * 100, stdout=log, stderr=subprocess.STDOUT, env=env(), timeout=120)
    if accepted.returncode:
        raise RuntimeError('SDK license setup failed; no emulator launched')
    report('installing_sdk_packages', packages=['emulator', 'platforms;android-35', IMAGE], emulator_launched=False)
    with (WORK / 'sdkmanager.log').open('wb') as log:
        result = subprocess.run(android_tool('sdkmanager', '--sdk_root=' + str(SDK), 'emulator', 'platforms;android-35', IMAGE),
                                stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT, env=env(), timeout=2400)
    if result.returncode:
        raise RuntimeError('SDK package installation failed; see private sdkmanager.log')
    if not all((SDK / path).is_file() for path in ['emulator/emulator', 'platform-tools/adb', 'platforms/android-35/android.jar', 'system-images/android-35/google_apis/arm64-v8a/system.img']):
        raise RuntimeError('SDK manager exited without installing the required emulator/image')
    report('sdk_packages_installed', emulator_present=(SDK / 'emulator/emulator').is_file(), emulator_launched=False)
    avd = WORK / 'avd' / (AVD_NAME + '.avd')
    if not (avd / 'config.ini').is_file():
        capture(android_tool('avdmanager', 'create', 'avd', '--name', AVD_NAME, '--package', IMAGE,
                             '--path', avd, '--device', 'pixel_6'), timeout=60, input=b'no\n')
    listed = capture([SDK / 'emulator/emulator', '-list-avds'])
    if AVD_NAME not in listed.splitlines():
        raise RuntimeError('Created AVD is not visible to the emulator')
    capture([SDK / 'emulator/emulator', '-accel-check'])
    result = dict(at=now(), avd=AVD_NAME, image=IMAGE, sdk=str(SDK), setup_complete=True,
                  emulator_launched=False, owner_authorized_install=True)
    save('setup.json', result)
    report('setup_complete', **{k: v for k, v in result.items() if k != 'at'})

def boot(approved, second=False, diagnostic=False, automatic=False):
    if not approved:
        raise RuntimeError('An explicit approval for this one launch is required')
    receipt = WORK / ('launch-20261020-automatic.json' if automatic else 'launch-20261020-diagnostic.json' if diagnostic else 'launch-20261020-second.json' if second else 'launch-20261020.json')
    if receipt.exists():
        raise RuntimeError('This one-launch approval was already consumed; never automatically restart')
    if not (WORK / 'setup.json').is_file():
        raise RuntimeError('Complete setup first')
    if not (SDK / 'platform-tools/adb').is_file():
        raise RuntimeError('The isolated SDK requires platform-tools before any launch')
    if not (SDK / 'platforms/android-35/android.jar').is_file():
        raise RuntimeError('The isolated SDK requires the Android platform before any launch')
    candidate, expected = APK, APK_SHA
    if automatic:
        prepared = json.loads((ROOT / 'release/google-play/evidence/appmetrica-automatic-apk-20261020.json').read_text())
        candidate, expected = Path(prepared['artifact']).resolve(), prepared['sha256']
        if not candidate.is_relative_to((ROOT / 'build/private').resolve()) or not prepared.get('localDiagnosticOnly') or prepared.get('label') != 'automatic':
            raise RuntimeError('Unexpected automatic measurement QA artifact')
    if hashlib.sha256(candidate.read_bytes()).hexdigest() != expected:
        raise RuntimeError('Prepared APK fingerprint mismatch')
    # Record consumption before starting the process, including failure paths.
    launch = dict(at=now(), avd=AVD_NAME, approval='Owner explicit automatic measurement QA launch approval 2026-10-20' if automatic else 'Owner explicit diagnostic APK launch approval 2026-10-20' if diagnostic else 'Owner explicit second launch approval 2026-10-20' if second else 'Owner 2026-10-20: Да, установить и проверить',
                  launch_attempted=True, automatic_restart_allowed=False)
    save(receipt.name, launch)
    with (WORK / ('emulator-automatic.log' if automatic else 'emulator-diagnostic.log' if diagnostic else 'emulator-second.log' if second else 'emulator.log')).open('ab') as log:
        process = subprocess.Popen([str(SDK / 'emulator/emulator'), '-avd', AVD_NAME, '-port', str(PORT),
                                    '-gpu', 'auto', '-no-snapshot', '-no-boot-anim', '-no-audio',
                                    '-camera-back', 'none', '-camera-front', 'none', '-memory', '2048', '-cores', '4'],
                                   stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT,
                                   env=env(), start_new_session=True)
    launch['pid'] = process.pid
    save(receipt.name, launch)
    report('emulator_process_started', pid=process.pid, automatic_restart_allowed=False)
    limit = time.monotonic() + 240
    while time.monotonic() < limit:
        if process.poll() is not None:
            raise RuntimeError('The approved emulator process exited; no automatic restart')
        try:
            if adb('shell', 'getprop', 'sys.boot_completed', timeout=10).strip() == '1':
                report('android_boot_complete', qemu=adb('shell', 'getprop', 'ro.kernel.qemu').strip(),
                       abi=adb('shell', 'getprop', 'ro.product.cpu.abi').strip())
                return
        except (RuntimeError, subprocess.TimeoutExpired):
            pass
        time.sleep(3)
    raise RuntimeError('Boot still pending after timeout; do not restart without a new approval')

def install():
    if adb('shell', 'getprop', 'ro.kernel.qemu').strip() != '1':
        raise RuntimeError('Refuse to install onto a physical device')
    if hashlib.sha256(APK.read_bytes()).hexdigest() != APK_SHA:
        raise RuntimeError('Prepared APK fingerprint mismatch')
    result = adb('install', '--no-streaming', '-r', str(APK), timeout=120)
    if 'Success' not in result:
        raise RuntimeError('APK install did not report success')
    adb('shell', 'am', 'start', '-n', 'com.multicastgames.catscuteminer/com.unity3d.player.UnityPlayerGameActivity')
    report('exact_release_apk_installed_and_started', apk_sha256=APK_SHA, package='com.multicastgames.catscuteminer')

def install_diagnostic(automatic=False):
    if adb('shell', 'getprop', 'ro.kernel.qemu').strip() != '1':
        raise RuntimeError('Diagnostic APK is scoped to the approved emulator')
    receipt = json.loads((ROOT / ('release/google-play/evidence/appmetrica-' + ('automatic' if automatic else 'diagnostic') + '-apk-20261020.json')).read_text())
    apk = Path(receipt['artifact']).resolve()
    if not apk.is_relative_to((ROOT / 'build/private').resolve()):
        raise RuntimeError('Unexpected diagnostic APK path')
    if not receipt.get('localDiagnosticOnly') or hashlib.sha256(apk.read_bytes()).hexdigest() != receipt.get('sha256'):
        raise RuntimeError('Diagnostic APK fingerprint mismatch')
    # The previous emulator fixture has an upload signature; this APK has a debug
    # signature. Reset only the disposable emulator fixture, never a phone install.
    adb('shell', 'am', 'force-stop', 'com.multicastgames.catscuteminer')
    existing = adb('shell', 'pm', 'list', 'packages', 'com.multicastgames.catscuteminer')
    if 'package:com.multicastgames.catscuteminer' in existing:
        adb('uninstall', 'com.multicastgames.catscuteminer')
    result = adb('install', '--no-streaming', str(apk), timeout=120)
    if 'Success' not in result:
        raise RuntimeError('Diagnostic APK install did not report success')
    adb('shell', 'am', 'start', '-n', 'com.multicastgames.catscuteminer/com.unity3d.player.UnityPlayerGameActivity')
    report('diagnostic_apk_installed_and_started', apk_sha256=receipt['sha256'],
           consent='fresh automatic AppMetrica; settings never opened' if automatic else 'fresh protected defaults; opt in through UI')

def diagnostics(label):
    if not re.fullmatch(r'[a-z0-9-]{1,60}', label):
        raise RuntimeError('Unsafe diagnostic label')
    if adb('shell', 'getprop', 'ro.kernel.qemu').strip() != '1':
        raise RuntimeError('Diagnostics are scoped to the approved emulator')
    evidence = ROOT / 'release/google-play/evidence'
    shot = evidence / ('appmetrica-emulator-' + label + '-20261020.png')
    image = subprocess.run([str(ADB), '-s', SERIAL, 'exec-out', 'screencap', '-p'], stdout=subprocess.PIPE,
                           stderr=subprocess.PIPE, env=env(), timeout=20, check=False)
    if image.returncode or not image.stdout.startswith(b'\x89PNG'):
        raise RuntimeError('Emulator screenshot capture failed')
    shot.write_bytes(image.stdout)
    package = 'com.multicastgames.catscuteminer'
    package_info = adb('shell', 'dumpsys', 'package', package)
    version = re.search(r'versionName=(\S+)', package_info)
    code = re.search(r'versionCode=(\d+)', package_info)
    pids = adb('shell', 'pidof', package).strip().split()
    raw = '\n'.join(adb('logcat', '-d', '-v', 'tag', '-t', '6000', '--pid=' + pid) for pid in pids)
    (WORK / ('logcat-' + label + '.log')).write_text(raw)
    mc = re.findall(r'\b\d+\.\d+ (?:SessionStart|SessionEnd|Foreground|Background|Focused|Unfocused|Consent|SdkInitStart|SdkInitResult|Event|Sent|CallbackFailed|Revenue|QueueFull|AdError) (?:Game|AppMetrica|Adjust|Meta|Max) detail=\d+ ms=\d+(?: event=[a-z_]+)?', raw)
    markers = ['ClassNotFoundException', 'NoClassDefFoundError', 'AndroidJavaException', 'NoSuchMethodError',
               'Failed to activate', 'SSLHandshakeException', 'UnknownHostException', 'JSONException',
               'FATAL EXCEPTION', 'Fatal signal', 'NullReferenceException', 'ExecutionEngineException']
    result = dict(at=now(), emulator=True, installed_version=version.group(1) if version else None,
                  installed_version_code=int(code.group(1)) if code else None, process_running=bool(pids),
                  screenshot_ref=str(shot.relative_to(ROOT)), mc_lines=mc[-120:],
                  error_marker_counts={marker: raw.count(marker) for marker in markers},
                  raw_logs_private=True, diagnostics_are_not_delivery_acknowledgements=True)
    result['sdk_submission_event_counts'] = dict(Counter(re.findall(r'Sent AppMetrica detail=\d+ ms=\d+ event=([a-z_]+)', raw)))
    result['sdk_initialization_lines'] = [line for line in mc if 'SdkInit' in line]
    result['appmetrica_diagnostic_lines'] = re.findall(r'^I/MC-AM\s*:\s*(\d+ [A-Za-z0-9_=; .-]+)$', raw, re.M)[-500:]
    approved_names = set(re.findall(r'case GameEvent\.\w+: return "([a-z_]+)";',
                                   (ROOT / 'Assets/Game/Scripts/Runtime/Integrations/GameSdkRuntime.cs').read_text()))
    native_names = re.findall(r'^I/AppMetrica\s*:\s*\[com\.multicastgames\.catscuteminer\]\s*:\s*\[[^\]\r\n]{0,80}\]\s*(?:\[\d+-[^\]\r\n]{0,160}\]\s*)?Event sent: ([a-z_]+)(?: with value .*)?$', raw, re.M)
    result['native_sdk_server_accepted_event_counts'] = dict(Counter(n for n in native_names if n in approved_names))
    result['dashboard_delivery_verified'] = False
    result['diagnostics_are_not_delivery_acknowledgements'] = not bool(result['native_sdk_server_accepted_event_counts'])
    (evidence / ('appmetrica-emulator-' + label + '-20261020.json')).write_text(json.dumps(result, indent=2) + '\n')
    summary = dict(result)
    summary['mc_lines'] = mc[-12:]
    summary['appmetrica_diagnostic_lines'] = result['appmetrica_diagnostic_lines'][-12:]
    print(json.dumps(summary), flush=True)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['setup', 'boot', 'install', 'install-diagnostic', 'install-automatic', 'diagnostics'])
    parser.add_argument('--approved-one-launch-20261020', action='store_true')
    parser.add_argument('--approved-second-launch-20261020', action='store_true')
    parser.add_argument('--approved-diagnostic-launch-20261020', action='store_true')
    parser.add_argument('--approved-automatic-launch-20261020', action='store_true')
    parser.add_argument('--label', default='initial')
    args = parser.parse_args()
    WORK.mkdir(parents=True, exist_ok=True, mode=0o700)
    if args.command == 'setup': setup()
    elif args.command == 'boot': boot(args.approved_one_launch_20261020 or args.approved_second_launch_20261020 or args.approved_diagnostic_launch_20261020 or args.approved_automatic_launch_20261020, args.approved_second_launch_20261020, args.approved_diagnostic_launch_20261020, args.approved_automatic_launch_20261020)
    elif args.command == 'install': install()
    elif args.command == 'install-diagnostic': install_diagnostic()
    elif args.command == 'install-automatic': install_diagnostic(automatic=True)
    elif args.command == 'diagnostics': diagnostics(args.label)

if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # Only this helper's fixed error messages; subprocess output stays private.
        print(json.dumps({'ok': False, 'error_type': type(error).__name__, 'stage': 'command_failed',
                          'detail': str(error) if isinstance(error, RuntimeError) else 'Local operation failed; see private logs'}), flush=True)
        sys.exit(1)
