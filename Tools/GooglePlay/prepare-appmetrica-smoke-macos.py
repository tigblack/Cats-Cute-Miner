"""Derive an AppMetrica QA APK from the existing verified AAB; never launch a device."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import threading
from types import SimpleNamespace
import zipfile

root = Path.cwd().resolve()
spec = importlib.util.spec_from_file_location('macos_builder', root / 'Tools/GooglePlay/google_play_macos.py')
builder = importlib.util.module_from_spec(spec)
spec.loader.exec_module(builder)
stage = 'aab_fingerprint'
tool_markers = {}

def main():
    global stage, tool_markers
    expected_aab = 'be10e6b2ae4aaf0518725f51b284e8bcaad218a7a880f7d01caf8d783334b759'
    aab = root / 'release/google-play/builds/cat-miners-0.1.4-5-noads.aab'
    if hashlib.sha256(aab.read_bytes()).hexdigest() != expected_aab:
        raise RuntimeError('Verified AAB fingerprint changed')
    private = root / 'build/private/appmetrica-smoke-0.1.4-5'
    stage = 'private_output_directory'
    if private.is_symlink() or (private.exists() and list(private.iterdir())):
        raise RuntimeError('Existing output must be inspected before another preparation')
    private.mkdir(mode=0o700, exist_ok=True)
    secret = '/Users/arthuri/MulticastSigning/com.multicastgames.catscuteminer/first-upload-20261018/signing.secret.json'
    stage = 'existing_signing_receipt_validation'
    env, expected_cert = builder.signing(SimpleNamespace(signing_json=secret), root)
    player = Path('/Applications/Unity/Hub/Editor/6000.6.4f1/PlaybackEngines/AndroidPlayer')
    archive = private / 'current.apks'
    java = player / 'OpenJDK/bin/java'
    args = [str(java), '-jar', str(player / 'Tools/bundletool-all-1.17.2.jar'), 'build-apks',
            '--bundle=' + str(aab), '--output=' + str(archive), '--mode=universal',
            '--ks=' + env[builder.PREFIX + 'KEYSTORE'], '--ks-key-alias=' + env[builder.PREFIX + 'ALIAS']]
    # Bundletool requires explicit password sources when there is no terminal.
    # Private FIFOs carry values in memory; no password is written to disk or argv.
    pipes = [private / '.store-password.pipe', private / '.key-password.pipe']
    def provide(pipe, value):
        with pipe.open('w') as stream:
            stream.write(value + '\n')
    for pipe, name, option in zip(pipes, ['STORE_PASSWORD', 'KEY_PASSWORD'], ['--ks-pass', '--key-pass']):
        os.mkfifo(pipe, mode=0o600)
        threading.Thread(target=provide, args=(pipe, env[builder.PREFIX + name]), daemon=True).start()
        args.append(option + '=file:' + str(pipe))
    stage = 'bundletool_build_apks'
    try:
        process = subprocess.run(args, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                 env=env, timeout=120, check=False)
    finally:
        for pipe in pipes:
            pipe.unlink(missing_ok=True)
    if process.returncode != 0:
        output = process.stdout.decode(errors='replace').lower()
        tool_markers = {marker: marker in output for marker in ['password', 'stdin', 'exception', 'keystore', 'unrecognized', 'aapt2', 'could not', 'not found']}
        raise RuntimeError('bundletool failed; private output suppressed')
    stage = 'extract_apk'
    with zipfile.ZipFile(archive) as bundle:
        apk_bytes = bundle.read('universal.apk')
    apk = private / 'cat-miners-0.1.4-5-from-aab.apk'
    with apk.open('xb') as stream:
        stream.write(apk_bytes)
    tools = sorted((player / 'SDK/build-tools').glob('*'), key=lambda p: tuple(map(int, re.findall(r'\d+', p.name))))[-1]
    check_env = os.environ.copy()
    check_env['JAVA_HOME'] = str(player / 'OpenJDK')
    def run(args):
        result = subprocess.run(list(map(str, args)), stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                env=check_env, timeout=30, check=False)
        if result.returncode != 0:
            raise RuntimeError('APK verification command failed; raw output suppressed')
        return result.stdout.decode(errors='replace')
    stage = 'verify_apk_signature'
    cert = run([tools / 'apksigner', 'verify', '--print-certs', apk])
    certs = re.findall(r'Signer #\d+ certificate SHA-256 digest: ([a-fA-F0-9]+)', cert)
    if len(certs) != 1 or certs[0].upper() != expected_cert:
        raise RuntimeError('Unexpected APK certificate')
    stage = 'verify_apk_identity'
    badging = run([tools / 'aapt2', 'dump', 'badging', apk])
    if not re.search(r"package: name='com\.multicastgames\.catscuteminer' versionCode='5' versionName='0\.1\.4'", badging):
        raise RuntimeError('Unexpected APK identity')
    stage = 'verify_apk_alignment'
    run([tools / 'zipalign', '-c', '-P', '16', '4', apk])
    with zipfile.ZipFile(apk) as bundle:
        abis = sorted({n.split('/')[1] for n in bundle.namelist() if n.startswith('lib/') and n.endswith('.so')})
    receipt = dict(aab_sha256=expected_aab, apk_sha256=hashlib.sha256(apk_bytes).hexdigest(),
                   apk_path_macos=str(apk), package='com.multicastgames.catscuteminer', version_name='0.1.4',
                   version_code=5, abis=abis, signature_verified=True, certificate_matches_existing_upload=True,
                   apk_zip_alignment_16kb_verified=True, source_recompiled=False, release_aab_changed=False,
                   emulator_installed=False, device_or_emulator_launched=False, telemetry_sent=False,
                   purpose='Exact existing release AAB derived APK, prepared for separately authorized emulator QA')
    (root / 'release/google-play/evidence/appmetrica-smoke-apk-prepared-20261020.json').write_text(json.dumps(receipt, indent=2) + '\n')
    print(json.dumps(receipt))

try:
    main()
except Exception as error:
    print(json.dumps({'prepared': False, 'stage': stage, 'error_type': type(error).__name__, 'tool_markers': tool_markers, 'reason': 'Preparation failed; private tool output and credential values suppressed', 'device_or_emulator_launched': False}))
    raise SystemExit(1)
