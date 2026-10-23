#!/usr/bin/env python3
"""Inspect a local AAB with Unity's bundled bundletool; no upload/device/private key access."""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import xml.etree.ElementTree as ET
import zipfile


def dex_classes(data):
    """Read defined classes, excluding optional SDK references in the string pool."""
    if len(data) < 112 or not data.startswith(b'dex\n'):
        raise ValueError('Unsupported DEX format')
    strings_size, strings_offset, types_size, types_offset = struct.unpack_from('<4I', data, 56)
    count, offset = struct.unpack_from('<2I', data, 96)
    result = set()
    for index in range(count):
        type_index = struct.unpack_from('<I', data, offset + index * 32)[0]
        if type_index >= types_size:
            raise ValueError('Invalid DEX class type')
        string_index = struct.unpack_from('<I', data, types_offset + type_index * 4)[0]
        if string_index >= strings_size:
            raise ValueError('Invalid DEX class descriptor')
        start = struct.unpack_from('<I', data, strings_offset + string_index * 4)[0]
        # Skip the ULEB128 UTF-16 length before the null-terminated descriptor.
        for _ in range(5):
            value = data[start]
            start += 1
            if value < 128:
                break
        else:
            raise ValueError('Invalid DEX string length')
        result.add(data[start:data.index(b'\0', start)])
    return result


def elf_loads(data):
    if data[:4] != b'\x7fELF' or data[5] != 1:
        raise ValueError('Unsupported native ELF format')
    bits = data[4]
    if bits == 2:
        offset = struct.unpack_from('<Q', data, 32)[0]
        stride, count = struct.unpack_from('<HH', data, 54)
        fmt = '<IIQQQQQQ'
    elif bits == 1:
        offset = struct.unpack_from('<I', data, 28)[0]
        stride, count = struct.unpack_from('<HH', data, 42)
        fmt = '<IIIIIIII'
    else:
        raise ValueError('Unsupported native ELF class')
    alignments = []
    for i in range(count):
        row = struct.unpack_from(fmt, data, offset + i * stride)
        if row[0] == 1:
            alignments.append(row[7])
    return alignments


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--aab', type=Path, required=True)
    p.add_argument('--java', type=Path, required=True)
    p.add_argument('--bundletool', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--version', required=True)
    p.add_argument('--version-code', required=True)
    args = p.parse_args()
    def dump(kind):
        r = subprocess.run([str(args.java), '-jar', str(args.bundletool), 'dump', kind,
                            '--bundle=' + str(args.aab)], capture_output=True, check=False)
        if r.returncode:
            raise RuntimeError('bundletool ' + kind + ' inspection failed; values not emitted')
        return r.stdout
    manifest = ET.fromstring(dump('manifest'))
    config = json.loads(dump('config'))
    android = '{http://schemas.android.com/apk/res/android}'
    app = manifest.find('application')
    sdk = manifest.find('uses-sdk')
    permissions = [e.get(android + 'name') for e in manifest.findall('uses-permission')]
    billing_version = next((e.get(android + 'value') for e in app.findall('meta-data')
                            if e.get(android + 'name') == 'com.google.android.play.billingclient.version'), None)
    meta_flags = {name: next((e.get(android + 'value') for e in app.findall('meta-data')
                             if e.get(android + 'name') == 'com.facebook.sdk.' + name), None)
                  for name in ['AutoInitEnabled', 'AutoLogAppEventsEnabled', 'AdvertiserIDCollectionEnabled']}
    libs = []
    dex_billing = dex_max = False
    with zipfile.ZipFile(args.aab) as bundle:
        for name in bundle.namelist():
            if name.startswith('base/lib/') and name.endswith('.so'):
                align = elf_loads(bundle.read(name))
                abi = name.split('/')[2]
                libs.append({'path': name, 'abi': abi, 'load_alignments': align,
                             'aligned_16kb': bool(align) and min(align) >= 16384})
            if name.startswith('base/dex/') and name.endswith('.dex'):
                data = bundle.read(name)
                classes = dex_classes(data)
                dex_billing |= b'Lcom/android/billingclient/api/BillingClient;' in classes
                dex_max |= any(name.startswith(b'Lcom/applovin/') for name in classes)
    abis = sorted(set(lib['abi'] for lib in libs))
    errors = []
    if manifest.get('package') != 'com.multicastgames.catscuteminer': errors.append('Unexpected package')
    if manifest.get(android + 'versionName') != args.version or manifest.get(android + 'versionCode') != args.version_code: errors.append('Unexpected version')
    if app.get(android + 'debuggable', 'false') != 'false': errors.append('Debuggable artifact')
    if sdk.get(android + 'targetSdkVersion') != '36': errors.append('Unexpected target SDK')
    if 'com.android.vending.BILLING' not in permissions or not billing_version or not dex_billing: errors.append('Billing native integration missing')
    if 'com.google.android.gms.permission.AD_ID' in permissions or dex_max: errors.append('Advertising SDK/permission present')
    if any(value != 'false' for value in meta_flags.values()): errors.append('Meta automatic collection is not disabled')
    if abis != ['arm64-v8a', 'armeabi-v7a']: errors.append('Required ARMv7 and ARM64 ABI set missing')
    for abi in abis:
        names = {lib['path'].split('/')[-1] for lib in libs if lib['abi'] == abi}
        if not {'libunity.so', 'libil2cpp.so', 'libmain.so'} <= names: errors.append('Incomplete Unity runtime for ' + abi)
    if any(not lib['aligned_16kb'] for lib in libs if lib['abi'] == 'arm64-v8a'): errors.append('ARM64 ELF below 16KB alignment')
    artifact_hash = hashlib.sha256(args.aab.read_bytes()).hexdigest()
    receipt_path = Path(str(args.aab) + '.build.json')
    build_receipt = json.loads(receipt_path.read_text()) if receipt_path.is_file() else None
    if build_receipt is not None and build_receipt.get('sha256') != artifact_hash:
        errors.append('Build receipt does not identify this AAB')
    report = {'artifact': str(args.aab), 'bytes': args.aab.stat().st_size,
              'sha256': artifact_hash,
              'package': manifest.get('package'), 'versionName': manifest.get(android + 'versionName'),
              'versionCode': manifest.get(android + 'versionCode'), 'minSdk': sdk.get(android + 'minSdkVersion'),
              'targetSdk': sdk.get(android + 'targetSdkVersion'), 'debuggable': app.get(android + 'debuggable', 'false'),
              'permissions': permissions, 'billingVersion': billing_version, 'billingDexPresent': dex_billing,
              'metaAutomaticFlags': meta_flags,
              'legalConfiguredAtBuild': build_receipt.get('legalConfigured') if build_receipt else None,
              'localArtifactOnly': build_receipt.get('localArtifactOnly') if build_receipt else None,
              'maxDexPresent': dex_max, 'abis': abis, 'nativeLibraries': libs,
              'bundleOptimizations': config.get('optimizations'), 'errors': errors,
              'status': 'artifact_static_checks_passed' if not errors else 'artifact_checks_failed',
              'signature': 'verified separately by guarded builder',
              'deviceOrEmulatorStarted': False, 'uploaded': False, 'publicationReady': False,
              'pending': ['In-app links/age consent, device launch, purchases and provider delivery',
                          'Generated APK ZIP alignment on 16KB devices and Console reconciliation']}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps({k: report[k] for k in ['status', 'bytes', 'package', 'versionName', 'versionCode', 'billingVersion', 'abis', 'errors']}))
    return 1 if errors else 0


if __name__ == '__main__':
    raise SystemExit(main())
