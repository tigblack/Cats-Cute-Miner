#!/usr/bin/env python3
"""Local macOS signed AAB workflow. Standard library only; no network or installs."""
import argparse
import base64
import fcntl
import hashlib
import json
import os
from pathlib import Path
import platform
import plistlib
import re
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
PACKAGE = 'com.multicastgames.catscuteminer'
PREFIX = 'CATMINERS_UPLOAD_'
SIGNING = ('KEYSTORE', 'ALIAS', 'STORE_PASSWORD', 'KEY_PASSWORD', 'CERT_SHA256')


class Blocked(Exception):
    pass


def require(condition, message):
    if not condition:
        raise Blocked(message)


def run(argv, env=None, timeout=60):
    # Never echo command lines, subprocess errors, preferences or private JSON.
    try:
        return subprocess.run([str(x) for x in argv], env=env, stdout=subprocess.PIPE,
                              stderr=subprocess.STDOUT, timeout=timeout, check=False)
    except (OSError, subprocess.TimeoutExpired):
        raise Blocked('Required local command could not run (or timed out); no raw output emitted.') from None


def read_json(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def fingerprint(value):
    return str(value).replace(':', '').upper()


def executable(path):
    return path.is_file() and os.access(str(path), os.X_OK)


def legal(root):
    config = read_json(root / 'Assets/Game/Resources/Integrations/cats-cute-miner-sdk.json')
    def valid(value, document):
        return value in (
            'https://sites.aimulticast.org/sargsyan/cats-cute-miner-legal/' + document + '/',
            'https://multicastgames.com/cats-cute-miner/' + document + '/',
        )
    # Same shape gate as GameSdkConfiguration.LegalValid. Unity's actual gate still runs.
    return (config.get('packageName') == PACKAGE and valid(config.get('privacyUrl'), 'privacy') and
            valid(config.get('termsUrl'), 'terms'))


def processes(ci=None):
    result = run(['/bin/ps', '-axo', 'pid=,comm=,args='])
    require(result.returncode == 0, 'Cannot inspect Unity processes; refusing build.')
    # Paths can contain spaces; match Unity's actual executable path, not arbitrary scripts.
    jobs = []
    for line in result.stdout.decode(errors='replace').splitlines():
        if re.search(r'/Unity(?:\.app/Contents/MacOS/Unity)?(?:\s|$)', line):
            # Asset import workers of the open main project use -batchMode too.
            # They do not own our isolated mirror. Any process on that mirror still blocks.
            worker = (re.search(r'(?:^|\s)-assetImportWorker(?:\s|$)', line, re.I) or
                      re.search(r'''(?:^|\s)-name\s+["']?AssetImportWorker[A-Za-z0-9]*["']?(?:\s|$)''', line, re.I))
            if (re.search(r'(?:^|\s)-batchmode(?:\s|$)', line, re.I) and not worker) or (ci and str(ci) in line):
                jobs.append(line)
    return len(jobs)


def inventory(args):
    root = args.project.resolve()
    version = re.search(r'm_EditorVersion:\s*(\S+)', (root / 'ProjectSettings/ProjectVersion.txt').read_text()).group(1)
    unity = Path(args.unity or os.environ.get('UNITY_EXE') or
                 '/Applications/Unity/Hub/Editor/{}/Unity.app/Contents/MacOS/Unity'.format(version)).resolve()
    contents = unity.parent.parent
    player = contents.parent.parent / 'PlaybackEngines/AndroidPlayer'
    blockers = []
    installed = None
    if (contents / 'Info.plist').is_file():
        with (contents / 'Info.plist').open('rb') as stream:
            installed = plistlib.load(stream).get('CFBundleVersion')
    if not executable(unity) or installed != version:
        blockers.append('Exact project Unity version/executable unavailable.')
    if not player.is_dir():
        blockers.append('AndroidPlayer module unavailable.')
    # defaults reads current cfprefsd domain instead of potentially stale on-disk plist.
    prefs_result = run(['/usr/bin/defaults', 'export', 'com.unity3d.UnityEditor5.x', '-'])
    require(prefs_result.returncode == 0, 'Cannot read Unity EditorPrefs domain; effective tool selection unknown.')
    prefs = plistlib.loads(prefs_result.stdout)
    tools = {}
    specs = [('SDK', 'SdkUseEmbedded', 'AndroidSdkRoot', ('ANDROID_SDK_ROOT', 'ANDROID_HOME')),
             ('NDK', 'NdkUseEmbedded', 'AndroidNdkRoot', ('ANDROID_NDK_ROOT', 'ANDROID_NDK_HOME')),
             ('OpenJDK', 'JdkUseEmbedded', 'JdkPath', ('JAVA_HOME',))]
    for name, flag, pref, envs in specs:
        embedded = prefs.get(flag, True)
        require(embedded in (True, False, 0, 1), 'Unknown Unity embedded-tool preference encoding.')
        selected = player / name if embedded else Path(str(prefs.get(pref, '')))
        selected_valid = selected.is_absolute()
        candidates = [{'source': 'bundled', 'path': str(player / name)}]
        for source, value in [('EditorPrefs', prefs.get(pref))] + [(key, os.environ.get(key)) for key in envs]:
            if isinstance(value, str) and value.startswith('/') and '\n' not in value:
                candidates.append({'source': source, 'path': value})
        missing = []
        bins = []
        if not selected_valid:
            missing.append('absolute selected path')
        if name == 'SDK':
            markers = ['platforms/android-36/android.jar']
            bins = [selected / 'platform-tools/adb']
            buildtools = sorted((selected / 'build-tools').glob('*'),
                                key=lambda p: tuple(int(n) for n in re.findall(r'\d+', p.name)))
            # Unity selects the newest installed build-tools; never fall back silently.
            bt = buildtools[-1] if buildtools else selected / 'build-tools/MISSING'
            bins += [bt / tool for tool in ('aapt2', 'zipalign', 'apksigner', 'd8')]
            markers += [str((bt / 'lib/apksigner.jar').relative_to(selected)),
                        str((bt / 'lib/d8.jar').relative_to(selected))]
        elif name == 'NDK':
            markers = ['source.properties', 'build/cmake/android.toolchain.cmake']
            hosts = sorted((selected / 'toolchains/llvm/prebuilt').glob('darwin-*'))
            bins = [host / 'bin/clang' for host in hosts] or [selected / 'toolchains/llvm/prebuilt/darwin-*/bin/clang']
        else:
            markers = ['release']
            bins = [selected / 'bin' / tool for tool in ('java', 'javac', 'jarsigner', 'keytool')]
        missing += [str(selected / marker) for marker in markers if not (selected / marker).is_file()]
        missing += [str(binary) for binary in bins if not executable(binary)]
        runnable = None
        if not missing and name in ('NDK', 'OpenJDK'):
            probe = run([bins[0], '--version' if name == 'NDK' else '-version'])
            runnable = probe.returncode == 0
            if not runnable:
                missing.append('selected host executable failed version probe (architecture/loader/toolchain)')
        tools[name] = dict(source='bundled' if embedded else 'EditorPrefs', selected=str(selected),
                           candidates=candidates, missing=missing, hostRunnable=runnable)
        if missing:
            blockers.append('Selected {} incomplete; see tools. Environment candidates are not automatically selected.'.format(name))
    legal_valid = legal(root)
    local_artifact_only = getattr(args, 'local_artifact_only', False)
    if not legal_valid and not local_artifact_only:
        blockers.append('Real game-specific privacy/terms configuration missing; Unity build must not start.')
    batch = processes(root / 'build/google-play-ci-macos')
    if batch:
        blockers.append('Another batch Unity or Editor on the CI mirror is running.')
    return dict(prebuildRequirements=['Verified private upload keystore and signing environment or explicit .secret.json.',
                                     'Certificate must match profile.release.signing_evidence_ref; builder never generates keys.',
                                     'Live adopted legal documents, all Unity release gates and unused Play version code.'],
                scope='read_only_macos_preflight', project=str(root), requiredUnity=version,
                installedUnity=installed, unityExe=str(unity), androidPlayer=str(player), tools=tools,
                legalConfigured=legal_valid, localArtifactOnly=local_artifact_only,
                publicationReady=False, conflictingUnityProcesses=batch, blockers=blockers,
                limitations=['Signing not inspected in preflight. Legal URL availability/adoption not verified.',
                             'No Unity launch, device query, installation or settings writes.',
                             'Provider gates, final native ABI/Billing/16 KB alignment, devices and Play checks remain pending.'])


def signing(args, root):
    env = os.environ.copy()
    if args.signing_json:
        path = Path(args.signing_json).expanduser().resolve()
        require(path.name.endswith('.secret.json') and not path.is_relative_to(root),
                'Signing JSON must be an explicitly passed .secret.json outside the project.')
        require(path.stat().st_mode & 0o077 == 0, 'Private signing JSON must have mode 600 (or stricter).')
        secret = read_json(path)
        require(secret.get('package') == PACKAGE, 'Signing JSON belongs to another package.')
        for key, field in zip(SIGNING, ('keystore', 'alias', 'storePassword', 'keyPassword', 'certSha256')):
            require(isinstance(secret.get(field), str) and secret[field], 'Private signing JSON has a missing field.')
            env[PREFIX + key] = secret[field]
    require(all(env.get(PREFIX + key) for key in SIGNING), 'Existing private signing environment incomplete; no key is created.')
    key = Path(env[PREFIX + 'KEYSTORE']).expanduser()
    require(key.is_absolute() and key.is_file() and not key.resolve().is_relative_to(root),
            'Existing keystore must be absolute and outside the project/mirror.')
    env[PREFIX + 'KEYSTORE'] = str(key.resolve())
    profile = read_json(root / 'release/google-play/profile.json')
    evidence_ref = profile.get('release', {}).get('signing_evidence_ref', 'release/google-play/evidence/upload-signing.json')
    require(isinstance(evidence_ref, str), 'Active signing evidence reference missing.')
    evidence_path = (root / evidence_ref).resolve()
    evidence_root = (root / 'release/google-play/evidence').resolve()
    require(evidence_path.is_relative_to(evidence_root) and evidence_path.suffix == '.json',
            'Active signing evidence must be a curated project receipt.')
    evidence = read_json(evidence_path)
    expected = fingerprint(env[PREFIX + 'CERT_SHA256'])
    require(evidence.get('package') == PACKAGE and
            evidence.get('status') in ('existing_upload_key_verified', 'first_upload_key_verified') and
            re.fullmatch(r'[A-F0-9]{64}', expected) and
            expected == fingerprint(evidence.get('certificateSha256')),
            'Expected certificate differs from the active verified signing receipt.')
    if evidence.get('status') == 'first_upload_key_verified':
        first_ref = evidence.get('first_upload_evidence_ref')
        require(isinstance(first_ref, str), 'First-upload Console evidence reference missing.')
        first_path = (root / first_ref).resolve()
        require(first_path.is_relative_to(evidence_root) and first_path.suffix == '.json',
                'First-upload evidence must be a curated project receipt.')
        first = read_json(first_path)
        require(first.get('package') == PACKAGE and first.get('play_app_id') == profile['release'].get('play_app_id') and
                first.get('upload_key_certificate_registered') is False and first.get('read_only') is True and
                first.get('observed_upload_key_message') == 'Certificate fingerprints will be shown here after you upload your first app bundle',
                'First-upload receipt does not establish the unbound initial upload certificate.')
    env[PREFIX + 'CERT_SHA256'] = expected
    return env, expected


def certificate(data):
    certs = re.findall(rb'-----BEGIN CERTIFICATE-----\s*(.*?)\s*-----END CERTIFICATE-----', data, re.S)
    require(len(certs) == 1, 'Expected exactly one upload certificate; inspect chain privately.')
    return hashlib.sha256(base64.b64decode(re.sub(rb'\s', b'', certs[0]), validate=True)).hexdigest().upper()


def retain_latest_aab(root, output, inspection_path):
    """Keep one game AAB, only after the replacement and its receipts verify."""
    policy_path = root / 'release/google-play/artifact-retention.json'
    if not policy_path.is_file():
        return []
    policy = read_json(policy_path)
    require(policy.get('package') == PACKAGE and policy.get('mode') == 'single_verified_aab',
            'Unexpected artifact retention policy; no previous files removed.')
    folder = root / 'release/google-play/builds'
    require(folder.resolve() == folder and output.parent == folder and not output.is_symlink(),
            'Single-artifact retention is restricted to the real project release builds folder.')
    name_pattern = r'cat-miners-\d+\.\d+\.\d+-(\d+)-noads(?:-r\d+)?\.aab'
    match = re.fullmatch(name_pattern, output.name)
    require(match is not None and output.is_file(), 'Unrecognized replacement AAB; no cleanup.')
    report = read_json(inspection_path)
    receipt = read_json(Path(str(output) + '.build.json'))
    digest = hashlib.sha256(output.read_bytes()).hexdigest()
    require(report.get('status') == 'artifact_static_checks_passed' and report.get('errors') == [] and
            report.get('sha256') == receipt.get('sha256') == digest and
            report.get('package') == receipt.get('package') == PACKAGE and
            receipt.get('signatureVerified') is True and
            str(report.get('versionCode')) == str(receipt.get('versionCode')) == match.group(1),
            'Replacement inspection/signature/identity/hash is incomplete; no previous files removed.')
    # Validate the entire removal set before touching any file. A newer or same-code
    # build is never assumed obsolete, and links never widen the cleanup scope.
    obsolete = []
    for candidate in sorted(folder.iterdir()):
        basename = candidate.name.removesuffix('.partial')
        old = re.fullmatch(name_pattern, basename)
        if not old or candidate == output:
            continue
        require(candidate.is_file() and not candidate.is_symlink(), 'Unrecognized artifact entry; no cleanup.')
        require(int(old.group(1)) < int(match.group(1)) or
                (candidate.name.endswith('.partial') and basename == output.name),
                'Another same/newer-code AAB exists; no cleanup.')
        obsolete.append(candidate)
    removed = []
    for candidate in obsolete:
        candidate.unlink()
        removed.append(candidate.name)
    return removed


def build(args):
    require(args.package == PACKAGE, 'Only accepted package com.multicastgames.catscuteminer is allowed.')
    require(re.fullmatch(r'\d+\.\d+\.\d+', args.version) and len(args.version) <= 50, 'Version must be MAJOR.MINOR.PATCH.')
    require(1 <= args.version_code <= 2100000000, 'Invalid version code; confirm unused in every Play track.')
    require(Path(args.output).is_absolute(), 'Output must be an absolute fresh .aab path.')
    output = Path(args.output).resolve()
    root = args.project.resolve()
    require(not output.is_relative_to(root / 'build/google-play-ci-macos') and output.suffix == '.aab',
            'Output must end in .aab and be outside the CI mirror.')
    outputs = [output, Path(str(output) + '.build.json'), Path(str(output) + '.unity.log')]
    require(not any(p.exists() or p.is_symlink() for p in outputs), 'Use a fresh AAB, receipt and log path.')
    probe = inventory(args)
    require(not probe['blockers'], 'Preflight blocked: ' + ' '.join(probe['blockers']))
    env, expected = signing(args, root)
    jdk = Path(probe['tools']['OpenJDK']['selected']) / 'bin'
    env['JAVA_HOME'] = str(jdk.parent)
    env['PATH'] = str(jdk) + os.pathsep + env.get('PATH', '')
    # Authenticate the existing private key BEFORE any mirror writes or Unity launch.
    cert = run([jdk / 'keytool', '-J-Duser.language=en', '-exportcert', '-rfc',
                '-keystore', env[PREFIX + 'KEYSTORE'], '-alias', env[PREFIX + 'ALIAS'],
                '-storepass:env', PREFIX + 'STORE_PASSWORD'], env=env)
    require(cert.returncode == 0 and certificate(cert.stdout) == expected,
            'Existing upload key cannot be authenticated or differs from approved certificate.')
    entry = run([jdk / 'keytool', '-J-Duser.language=en', '-list', '-v',
                 '-keystore', env[PREFIX + 'KEYSTORE'], '-alias', env[PREFIX + 'ALIAS'],
                 '-storepass:env', PREFIX + 'STORE_PASSWORD'], env=env)
    require(entry.returncode == 0 and b'PrivateKeyEntry' in entry.stdout,
            'Approved signing alias must contain an existing private key, not just a trusted certificate.')
    ci = root / 'build/google-play-ci-macos'
    require(ci.resolve() == ci and not ci.parent.is_symlink(), 'CI mirror/build directory must not be a symlink.')
    ci.parent.mkdir(parents=True, exist_ok=True)
    lock_path = ci.parent / '.google-play-macos.lock'
    # The persistent inode is intentional: unlinking a flock file allows competing owners.
    with lock_path.open('a') as lock:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise Blocked('Another macOS wrapper owns the build lock.') from None
        def unlocked():
            require(processes(ci) == 0, 'Another Unity batch job or Editor on the mirror is running.')
            require(not (ci / 'Temp/UnityLockfile').exists(),
                    'CI UnityLockfile exists; inspect it manually after confirming Unity stopped. Never auto-remove locks.')
        unlocked()
        ci.mkdir(exist_ok=True)
        for folder in ('Assets', 'Packages', 'ProjectSettings'):
            require(not (ci / folder).is_symlink(), 'Mirror source directory is a symlink; refusing copy.')
            result = run(['/usr/bin/rsync', '-a', '--delete', str(root / folder) + '/', str(ci / folder) + '/'], timeout=600)
            require(result.returncode == 0, 'Source mirror copy failed; no Unity launched.')
        # Reject symlinks that could cause writes outside the mirror or import live source.
        for folder in ('Assets', 'Packages', 'ProjectSettings'):
            require(not any(p.is_symlink() for p in (ci / folder).rglob('*')), 'Mirror contains symlinks; review isolation before building.')
        manifest = read_json(ci / 'Packages/manifest.json')
        for value in manifest.get('dependencies', {}).values():
            if isinstance(value, str) and value.startswith('file:'):
                target = (ci / 'Packages' / value[5:]).resolve()
                require(target.is_relative_to(ci) and target.exists(), 'Local package dependency escapes/misses mirror; vendor it before building.')
        require(legal(ci) or args.local_artifact_only, 'Mirrored legal configuration missing; Unity must not start.')
        unlocked()
        require(not any(p.exists() for p in outputs), 'Output path became occupied; refusing overwrite.')
        output.parent.mkdir(parents=True, exist_ok=True)
        started = False
        try:
            # Exclusive reservation protects the log path and leaves a failed-run breadcrumb.
            with outputs[2].open('x'):
                pass
            argv = [probe['unityExe'], '-batchmode', '-nographics', '-projectPath', str(ci),
                    '-buildTarget', 'Android', '-executeMethod', 'CatMiners.Editor.GooglePlay.BuildAndroid.Aab',
                    '-logFile', str(outputs[2]), '-catminersPackage', PACKAGE, '-catminersVersion', args.version,
                    '-catminersVersionCode', str(args.version_code), '-catminersOutput', str(output)]
            if args.local_artifact_only:
                argv.append('-catminersLocalArtifactOnly')
            started = True
            # Never pipe Unity logs to stdout; keep all failed logs private on disk.
            with outputs[2].open('ab') as log:
                process = subprocess.Popen(argv, env=env, stdout=log, stderr=log)
                try:
                    code = process.wait()
                except BaseException:
                    process.terminate()
                    process.wait()
                    raise
            require(code == 0 and output.is_file() and output.stat().st_size > 0 and outputs[1].is_file(),
                    'Unity failed or fresh AAB/receipt missing; inspect preserved private .unity.log.')
            report = read_json(outputs[1])
            fields = dict(result='BuiltPendingArtifactVerification', path=str(output), package=PACKAGE,
                          version=args.version, versionCode=args.version_code, targetApi=36,
                          architecture='ARMv7+ARM64', backend='IL2CPP', development=False,
                          signatureVerified=True, unity=probe['requiredUnity'], scene='Assets/Game/Scenes/CoreGame.unity')
            require(all(report.get(k) == v for k, v in fields.items()) and
                    fingerprint(report.get('uploadCertificateSha256')) == expected and report.get('verificationPending'),
                    'Fresh release receipt disagrees with requested guarded release.')
            require(report.get('localArtifactOnly') == args.local_artifact_only and
                    report.get('legalConfigured') == legal(ci) and report.get('publicationReady') is False,
                    'Artifact purpose or legal readiness disagrees with the explicit local build request.')
            digest = hashlib.sha256()
            with output.open('rb') as artifact:
                for chunk in iter(lambda: artifact.read(1024 * 1024), b''):
                    digest.update(chunk)
            require(digest.hexdigest() == report.get('sha256'), 'AAB hash disagrees with receipt.')
            verify = run([jdk / 'jarsigner', '-J-Duser.language=en', '-J-Duser.country=US', '-verify', output], env=env)
            require(verify.returncode == 0 and re.search(rb'(?m)^jar verified\.', verify.stdout) and
                    not re.search(rb'unsigned entries|jar is unsigned', verify.stdout, re.I),
                    'Final AAB unsigned, partly unsigned or invalid; output rejected.')
            cert = run([jdk / 'keytool', '-J-Duser.language=en', '-printcert', '-jarfile', output, '-rfc'], env=env)
            require(cert.returncode == 0 and certificate(cert.stdout) == expected,
                    'Final AAB certificate differs from approved existing signing evidence.')
            inspection = root / 'release/google-play/evidence/build-history' / (output.name + '.inspection.json')
            bundletool = Path(probe['androidPlayer']) / 'Tools/bundletool-all-1.17.2.jar'
            checked = run([sys.executable, root / 'Tools/GooglePlay/inspect-aab.py', '--aab', output,
                           '--java', jdk / 'java', '--bundletool', bundletool, '--output', inspection,
                           '--version', args.version, '--version-code', str(args.version_code)], timeout=120)
            require(checked.returncode == 0, 'Final native AAB inspection failed; older artifacts kept.')
            print('Signed AAB and hash verified locally: ' + str(output))
            print('Manifest/Billing/ABI/16 KB ELF checks passed: ' + str(inspection))
            print('Pending: device/provider checks, generated APK alignment and Console version/signing review. Not published.')
        except BaseException:
            if started:
                for path in outputs[:2]:
                    path.unlink(missing_ok=True)
            raise
        # Cleanup failure must preserve the already verified new artifact as well.
        removed = retain_latest_aab(root, output, inspection)
        print('Single-AAB retention: removed ' + str(len(removed)) + ' obsolete artifacts.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='mode', required=True)
    for mode in ('preflight', 'build'):
        p = sub.add_parser(mode)
        p.add_argument('--project', type=Path, default=ROOT)
        p.add_argument('--unity', help='Exact Unity executable; otherwise UNITY_EXE/project version')
        p.add_argument('--local-artifact-only', action='store_true',
                       help='Build a signed local artifact while legal publication is pending; does not enable SDKs or permit upload.')
        if mode == 'build':
            p.add_argument('--package', default=PACKAGE)
            p.add_argument('--version', required=True)
            p.add_argument('--version-code', type=int, required=True)
            p.add_argument('--output', required=True)
            p.add_argument('--signing-json', help='Explicit existing private .secret.json outside project; never sourced as shell')
    args = parser.parse_args()
    try:
        require(platform.system() == 'Darwin', 'Run these wrappers on macOS (use on-remote from the hub).')
        require(sys.version_info >= (3, 9), 'Python 3.9+ is required; nothing is installed automatically.')
        os.umask(0o077)
        if args.mode == 'preflight':
            report = inventory(args)
            print(json.dumps(report, indent=2))
            return 1 if report['blockers'] else 0
        build(args)
        return 0
    except Blocked as error:
        print('Blocked: ' + str(error), file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        print('Interrupted; failed Unity log preserved.', file=sys.stderr)
        return 130
    except Exception:
        print('Blocked: local configuration/tool failure; raw exception suppressed to protect secrets.', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
