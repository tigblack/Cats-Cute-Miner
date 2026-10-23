"""Build a local diagnostic APK in an isolated mirror. Never launches an emulator."""
import argparse
from datetime import datetime, timezone
import fcntl
import hashlib
import json
import os
import re
from pathlib import Path
import subprocess
import sys

from google_play_macos import ROOT, Blocked, inventory, processes, require, run


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', default='0.1.5')
    parser.add_argument('--version-code', type=int, default=6)
    parser.add_argument('--label', default='automatic')
    cli = parser.parse_args()
    require(re.fullmatch(r'\d+\.\d+\.\d+', cli.version) and 0 < cli.version_code <= 2100000000,
            'Supply a valid diagnostic version.')
    require(re.fullmatch(r'[a-z-]{1,40}', cli.label), 'Supply a safe diagnostic label.')
    args = argparse.Namespace(project=ROOT, unity=None, local_artifact_only=True)
    probe = inventory(args)
    require(not probe['blockers'], 'Diagnostic preflight failed: ' + ', '.join(probe['blockers']))
    ci = ROOT / 'build/google-play-ci-macos'
    output = ROOT / ('build/private/appmetrica-{}-{}-{}-20261020'.format(cli.label, cli.version, cli.version_code)) / ('cat-miners-appmetrica-' + cli.label + '.apk')
    require(not output.exists(), 'Diagnostic APK already exists; keep it or choose a new explicit output.')
    ci.parent.mkdir(parents=True, exist_ok=True)
    with (ci.parent / '.google-play-macos.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        require(processes(ci) == 0 and not (ci / 'Temp/UnityLockfile').exists(), 'Another Unity owns the build mirror.')
        ci.mkdir(exist_ok=True)
        for folder in ('Assets', 'Packages', 'ProjectSettings'):
            require(not (ci / folder).is_symlink(), 'Mirror folder must not be a symlink.')
            result = run(['/usr/bin/rsync', '-a', '--delete', str(ROOT / folder) + '/', str(ci / folder) + '/'], timeout=600)
            require(result.returncode == 0, 'Diagnostic source mirror failed.')
        output.parent.mkdir(parents=True, exist_ok=True)
        log = output.parent / 'build.private.log'
        argv = [probe['unityExe'], '-batchmode', '-nographics', '-projectPath', str(ci), '-buildTarget', 'Android',
                '-executeMethod', 'CatMiners.Editor.GooglePlay.BuildAppMetricaDiagnostic.Apk',
                '-catminersDiagnosticOutput', str(output), '-catminersDiagnosticVersion', cli.version,
                '-catminersDiagnosticVersionCode', str(cli.version_code), '-logFile', str(log)]
        print('Building local AppMetrica diagnostic APK; no emulator launch.', flush=True)
        with log.open('wb') as stream:
            result = subprocess.run(argv, stdout=stream, stderr=stream, timeout=3600)
        require(result.returncode == 0 and output.is_file(), 'Diagnostic APK build failed; see private build log.')
        receipt = dict(at=datetime.now(timezone.utc).isoformat(), artifact=str(output),
                       sha256=hashlib.sha256(output.read_bytes()).hexdigest(), bytes=output.stat().st_size,
                       localDiagnosticOnly=True, nativeVerboseSdkLogs=True, readOnlyConsole=True,
                       productionAabChanged=False, emulatorLaunched=False, runtimeVerification='pending')
        receipt.update(versionName=cli.version, versionCode=cli.version_code, label=cli.label)
        (ROOT / ('release/google-play/evidence/appmetrica-' + cli.label + '-apk-20261020.json')).write_text(json.dumps(receipt, indent=2) + '\n')
        print(json.dumps(receipt), flush=True)


if __name__ == '__main__':
    try:
        main()
    except (Blocked, OSError, subprocess.TimeoutExpired) as error:
        print('Diagnostic build stopped: ' + (str(error) if isinstance(error, Blocked) else type(error).__name__))
        sys.exit(1)
