#!/usr/bin/env python3
"""Build an installable, non-development APK in the existing isolated Android checkout."""
import argparse
import fcntl
import hashlib
import importlib.util
import json
import os
import platform
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    loaded = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(loaded)
    return loaded


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', required=True)
    parser.add_argument('--version-code', type=int, required=True)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--reports', required=True, type=Path)
    parser.add_argument('--prepare-only', action='store_true')
    parser.add_argument('--verify-only', action='store_true', help='Verify an existing APK without launching Unity.')
    args = parser.parse_args()
    assert platform.system() == 'Darwin', 'Run on the connected Mac.'
    assert re.fullmatch(r'\d+\.\d+\.\d+', args.version) and args.version_code > 0
    output = (ROOT / args.output).resolve()
    reports = (ROOT / args.reports).resolve()
    assert output.is_relative_to(ROOT / 'Builds') and output.suffix == '.apk'
    assert not (args.prepare_only and args.verify_only), 'Choose preparation or existing-artifact verification.'
    assert output.is_file() if args.verify_only else not output.exists(), 'Expected an existing APK only with --verify-only.'
    assert reports.is_relative_to(ROOT / 'Docs/Reports')
    reports.mkdir(parents=True, exist_ok=True)
    source_sdk = json.loads((ROOT / 'Assets/Game/Resources/Integrations/cats-cute-miner-sdk.json').read_text())
    app_id = source_sdk.get('admobAndroidAppId', '')
    assert re.fullmatch(r'ca-app-pub-[0-9]{16}~[0-9]{10}', app_id) and not app_id.startswith('ca-app-pub-3940256099942544~'), \
        'The game\'s own AdMob Android app ID is required for Google UMP; no sample ID is substituted.'
    release = module('release_tools', ROOT / 'Tools/GooglePlay/google_play_macos.py')
    probe = release.inventory(argparse.Namespace(project=ROOT, unity=None, local_artifact_only=True))
    assert not probe['blockers'], probe['blockers']
    ci = ROOT / 'build/google-play-ci-macos'
    ci.parent.mkdir(exist_ok=True)
    with (ci.parent / '.google-play-macos.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        assert release.processes(ci) == 0 and not (ci / 'Temp/UnityLockfile').exists(), 'Android checkout busy.'
        ci.mkdir(exist_ok=True)
        if not args.verify_only:
            for folder in ('Assets', 'Packages', 'ProjectSettings'):
                assert not (ci / folder).is_symlink()
                subprocess.run(['/usr/bin/rsync', '-a', '--delete', str(ROOT / folder) + '/', str(ci / folder) + '/'], check=True)
            assert not any(p.is_symlink() for folder in ('Assets', 'Packages', 'ProjectSettings') for p in (ci / folder).rglob('*'))
        env = os.environ.copy()
        env['JAVA_HOME'] = probe['tools']['OpenJDK']['selected']
        env['PATH'] = str(Path(env['JAVA_HOME']) / 'bin') + os.pathsep + env.get('PATH', '')

        def unity(method, name, extra=()):
            log = reports / (name + '.log')
            command = [probe['unityExe'], '-batchmode', '-nographics', '-projectPath', str(ci),
                       '-buildTarget', 'Android', '-executeMethod', method, '-logFile', str(log),
                       '-catminersReports', str(reports), *extra]
            print('Unity ' + name + '; log=' + str(log), flush=True)
            with log.open('ab') as stream:
                result = subprocess.run(command, env=env, stdout=stream, stderr=stream)
            assert result.returncode == 0, 'Unity ' + name + ' failed; inspect its private log.'

        if not args.verify_only:
            unity('CatMiners.Editor.GooglePlay.AndroidRewardedBuild.Prepare', 'prepare')
            if args.prepare_only:
                return
            output.parent.mkdir(parents=True, exist_ok=True)
            unity('CatMiners.Editor.GooglePlay.BuildAndroid.InstallApk', 'build',
                  ['-catminersLocalArtifactOnly', '-catminersPackage', release.PACKAGE,
                   '-catminersVersion', args.version, '-catminersVersionCode', str(args.version_code),
                   '-catminersOutput', str(output)])
        assert output.is_file()
        digest = hashlib.sha256(output.read_bytes()).hexdigest()
        build_receipt = json.loads(Path(str(output) + '.build.json').read_text())
        assert (build_receipt['sha256'] == digest and build_receipt['version'] == args.version
                and build_receipt['versionCode'] == args.version_code and build_receipt['localInstall'] is True), \
            'APK differs from its Unity build receipt.'
        sdk = Path(probe['tools']['SDK']['selected'])
        tools = sorted((sdk / 'build-tools').glob('*'))[-1]
        signed = release.run([tools / 'apksigner', 'verify', '--verbose', '--print-certs', output], env=env)
        assert signed.returncode == 0, 'APK signature verification failed.'
        aligned = release.run([tools / 'zipalign', '-c', '-P', '16', '4', output], env=env)
        assert aligned.returncode == 0, 'APK ZIP alignment verification failed.'
        badging = release.run([tools / 'aapt', 'dump', 'badging', output], env=env).stdout.decode()
        assert "package: name='" + release.PACKAGE + "'" in badging
        assert "versionName='" + args.version + "'" in badging
        assert "versionCode='" + str(args.version_code) + "'" in badging
        assert "targetSdkVersion:'36'" in badging and 'application-debuggable' not in badging
        assert 'com.google.android.gms.permission.AD_ID' in badging and 'com.android.vending.BILLING' in badging
        manifest_dump = release.run([tools / 'aapt', 'dump', 'xmltree', output, 'AndroidManifest.xml'], env=env)
        assert manifest_dump.returncode == 0 and app_id.encode() in manifest_dump.stdout
        # Android resource optimization can rename the ZIP entry (for example res/Bc.json).
        # Resolve the exact runtime resource from its table instead of guessing a file path.
        resources = release.run([tools / 'aapt', 'dump', '--values', 'resources', output], env=env)
        assert resources.returncode == 0, 'Cannot inspect APK resource table.'
        settings_paths = set(re.findall(
            r'(?m)^\s*resource 0x[0-9a-f]+ ' + re.escape(release.PACKAGE)
            + r':raw/applovin_settings:[^\n]*\n\s*\(string(?:8|16)?\) "([^"\n]+)"',
            resources.stdout.decode()))
        assert len(settings_paths) == 1, 'Expected one native MAX configuration resource.'
        settings_path = settings_paths.pop()
        import zipfile
        inspector = module('artifact_inspection', ROOT / 'Tools/GooglePlay/inspect-aab.py')
        with zipfile.ZipFile(output) as apk:
            classes = set().union(*(inspector.dex_classes(apk.read(n)) for n in apk.namelist() if n.endswith('.dex')))
            assert any(n.startswith(b'Lcom/applovin/') for n in classes), 'Native MAX absent.'
            assert any(n.startswith(b'Lcom/google/android/ump/') for n in classes), 'Native consent provider absent.'
            assert b'Lcom/android/billingclient/api/BillingClient;' in classes
            assert settings_path in apk.namelist(), 'Native MAX configuration resource missing from APK.'
            native_config = json.loads(apk.read(settings_path))
            source_config = json.loads((ROOT / 'Assets/Game/Resources/Integrations/cats-cute-miner-sdk.json').read_text())
            flow = native_config['consent_flow_settings']
            assert native_config['sdk_key'] == source_config['maxSdkKey']
            assert flow['consent_flow_enabled'] is True
            assert flow['consent_flow_privacy_policy'] == source_config['privacyUrl']
            assert flow['consent_flow_terms_of_service'] == source_config['termsUrl']
            assert 'consent_flow_debug_user_geography' not in flow
            libs = [{'path': n, 'loadAlignments': inspector.elf_loads(apk.read(n))} for n in apk.namelist() if n.startswith('lib/') and n.endswith('.so')]
            assert {n['path'].split('/')[1] for n in libs} == {'arm64-v8a', 'armeabi-v7a'}
            assert all(n['loadAlignments'] and min(n['loadAlignments']) >= 16384 for n in libs if '/arm64-v8a/' in n['path'])
        receipt = {'version': args.version, 'versionCode': args.version_code, 'package': release.PACKAGE,
                   'path': str(output), 'bytes': output.stat().st_size, 'sha256': digest, 'buildReceiptMatches': True,
                   'signatureVerified': True, 'signing': 'Android local install key', 'development': False,
                   'maxDexPresent': True, 'billingDexPresent': True, 'zipAlignment16KB': True, 'nativeLibraries': libs,
                   'googleCmpDexPresent': True, 'nativeConsentConfigMatches': True, 'advertisingIdPermissionPresent': True,
                   'nativeConsentConfigResource': settings_path,
                   'deviceAdDeliveryVerified': False, 'publishedToPlay': False}
        (reports / 'verified-apk.json').write_text(json.dumps(receipt, indent=2) + '\n')
        print('Installable APK verified: ' + str(output), flush=True)


if __name__ == '__main__':
    try:
        main()
    except AssertionError as error:
        raise SystemExit('Blocked: ' + str(error)) from None
