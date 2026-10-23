#!/usr/bin/env python3
"""Build the full public WebGL game on Mac in an isolated, credential-free mirror."""
import argparse
import fcntl
import gzip
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import subprocess
import sys
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[2]


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def build(args):
    require(platform.system() == 'Darwin', 'Run this builder on the connected Mac.')
    root = ROOT.resolve()
    version = re.search(r'm_EditorVersion:\s*(\S+)',
                        (root / 'ProjectSettings/ProjectVersion.txt').read_text()).group(1)
    unity = Path('/Applications/Unity/Hub/Editor') / version / 'Unity.app/Contents/MacOS/Unity'
    require(unity.is_file(), 'The exact Unity editor is unavailable.')
    require((unity.parent.parent.parent.parent / 'PlaybackEngines/WebGLSupport').is_dir(),
            'WebGL Build Support is unavailable.')
    require(re.fullmatch(r'\d+\.\d+\.\d+', args.version), 'Use a numeric release version.')
    output = (root / args.output).resolve()
    require(output.is_relative_to(root / 'Builds') and not output.exists(),
            'Use a fresh output directory inside Builds; previous builds are preserved.')
    ci = root / 'build/web-ci-macos'
    require(not ci.is_symlink() and not ci.parent.is_symlink(), 'The mirror must not be a symlink.')
    ci.mkdir(parents=True, exist_ok=True)
    with (ci.parent / '.webgl-build.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        jobs = subprocess.run(['/bin/ps', '-axo', 'args='], capture_output=True, text=True, check=True)
        for line in jobs.stdout.splitlines():
            if not re.search(r'/Contents/MacOS/Unity(?:\s|$)', line):
                continue
            worker = '-assetimportworker' in line.lower() or 'AssetImportWorker' in line
            require(not (str(ci) in line or ('-batchmode' in line.lower() and not worker)),
                    'Another Unity batch job or an editor owning this mirror is running.')
        require(not (ci / 'Temp/UnityLockfile').exists(), 'The mirror is locked; inspect its owner.')
        for folder in ('Assets', 'Packages', 'ProjectSettings'):
            require(not (ci / folder).is_symlink(), 'Mirror source directory must not be a symlink.')
            subprocess.run(['/usr/bin/rsync', '-a', '--delete', str(root / folder) + '/',
                            str(ci / folder) + '/'], check=True, stdout=subprocess.DEVNULL)
            require(not any(p.is_symlink() for p in (ci / folder).rglob('*')),
                    'Mirror source contains a symlink.')

        # Native Android services do not run on the web. Do not bundle their client keys as Resources.
        sdk_path = ci / 'Assets/Game/Resources/Integrations/cats-cute-miner-sdk.json'
        sdk = json.loads(sdk_path.read_text())
        keys = ('appMetricaKey', 'adjustToken', 'metaClientToken', 'maxSdkKey', 'rewardedAdUnit')
        private_values = [sdk[k] for k in keys if isinstance(sdk.get(k), str) and sdk[k]]
        public_sdk = {k: sdk[k] for k in ('packageName', 'privacyUrl', 'termsUrl')}
        sdk_path.write_text(json.dumps(public_sdk, indent=2) + '\n')
        removed_settings = []
        for name in ('FacebookSettings.asset', 'AppLovinSettings.asset'):
            for path in (ci / 'Assets').rglob(name):
                removed_settings.append(str(path.relative_to(ci)))
                path.unlink()
                Path(str(path) + '.meta').unlink(missing_ok=True)

        # Public web builds have neither a native payment store nor a rewarded-ad provider.
        # Disable the legacy simulator; gem-priced gameplay purchases remain available.
        store_path = ci / 'Assets/Game/Resources/Outpost/store_catalog.json'
        store_text = store_path.read_text()
        require('"TestPurchases": true' in store_text, 'Review changed store configuration before building.')
        store_text = store_text.replace('"TestPurchases": true', '"TestPurchases": false')
        store_path.write_text(store_text)
        defaults = ci / 'Assets/Game/Scripts/Core/Outpost/Config/OutpostConfigSet.Defaults.cs'
        defaults_text = defaults.read_text()
        require('""TestPurchases"": true' in defaults_text, 'Embedded store configuration changed.')
        defaults.write_text(defaults_text.replace('""TestPurchases"": true', '""TestPurchases"": false'))
        settings = ci / 'ProjectSettings/ProjectSettings.asset'
        text, count = re.subn(r'(?m)^  bundleVersion:.*$', '  bundleVersion: ' + args.version, settings.read_text())
        require(count == 1, 'Cannot set the web release version.')
        settings.write_text(text)

        reports = root / (args.reports or datetime.now(timezone.utc).strftime('Docs/Reports/webgl-%Y%m%d'))
        reports.mkdir(parents=True, exist_ok=True)
        stamp = datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S')
        log = root / ('build/ci-logs/WebGL-' + stamp + '.log')
        log.parent.mkdir(parents=True, exist_ok=True)
        print('Building full WebGL release ' + args.version + '; log=' + str(log), flush=True)
        command = [str(unity), '-batchmode', '-nographics', '-projectPath', str(ci),
                   '-buildTarget', 'WebGL', '-executeMethod', 'CatMiners.Editor.BuildWeb.Pages',
                   '-logFile', str(log), '-catminersOutput', str(output), '-catminersReports', str(reports)]
        with log.open('ab') as stream:
            code = subprocess.run(command, stdout=stream, stderr=stream).returncode
        require(code == 0, 'Unity WebGL build failed; inspect the private build log.')
        report_path = reports / 'build-web.json'
        report = json.loads(report_path.read_text())
        require(report.get('result') == 'Succeeded' and report.get('path') == str(output),
                'A fresh successful build report is required.')
        require((output / 'index.html').is_file(), 'The build has no index.html.')
        files = sorted(p for p in output.rglob('*') if p.is_file())
        require(not any(p.is_symlink() for p in output.rglob('*')), 'Public output contains a symlink.')
        require(sum(p.stat().st_size for p in files) < 100 * 1024 * 1024, 'Public output exceeds 100 MiB.')
        for path in files:
            data = path.read_bytes()
            if data[:2] == b'\x1f\x8b':
                data = gzip.decompress(data)
            require(not any(value.encode() in data or value.encode('utf-16-le') in data
                            for value in private_values),
                    'A native SDK client key remains in generated output: ' + str(path.relative_to(output)))
        verification = dict(
            at=datetime.now(timezone.utc).isoformat(), status='verified_full_game_webgl_release',
            version=args.version, unity=version, output=str(output), development=False,
            playable=False, balanceVersion=json.loads((ci / 'Assets/Game/Resources/Outpost/outpost_balance.json').read_text())['Version'],
            nativeSdkConfigurationRemoved=True, removedNativeSettings=removed_settings,
            knownNativeKeysAbsentFromOutput=True, simulatedMoneyAndRewardedAdsDisabled=True,
            mainProjectAssetsUnchanged=True, sizeBytes=sum(p.stat().st_size for p in files),
            files=[dict(path=str(p.relative_to(output)), bytes=p.stat().st_size,
                        sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in files],
            buildReportRef=str(report_path.relative_to(root)), published=False)
        (reports / 'verified-build.json').write_text(json.dumps(verification, indent=2) + '\n')
        print('Full WebGL release verified: ' + str(output), flush=True)
        print('Public files: ' + str(len(files)) + '; bytes=' + str(verification['sizeBytes']), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', required=True)
    parser.add_argument('--output', default='Builds/Web')
    parser.add_argument('--reports', help='Project-relative evidence directory; defaults to today under Docs/Reports.')
    try:
        build(parser.parse_args())
    except (RuntimeError, OSError, subprocess.CalledProcessError, ValueError) as error:
        print('WebGL build stopped: ' + str(error), file=sys.stderr)
        sys.exit(1)
