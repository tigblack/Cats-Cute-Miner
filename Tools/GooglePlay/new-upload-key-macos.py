#!/usr/bin/env python3
"""Explicit first-upload key preparation only. Python 3.9+, macOS, stdlib."""

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import secrets
import stat
import subprocess
import sys
from urllib.parse import urlsplit


ROOT = Path(__file__).resolve().parents[2]
PACKAGE = "com.multicastgames.catscuteminer"
ALIAS = "upload"
RECEIPT = ROOT / "release/google-play/evidence/google-play-signing-current-20261018.json"
FIRST_UPLOAD_MESSAGE = (
    "Certificate fingerprints will be shown here after you upload your first app bundle"
)
# Contract checked against the parent's actual authenticated native Console
# readback. Never create or modify that public evidence in this helper.
REQUIRED_EVIDENCE = {
    "schema_version": 1,
    "package": PACKAGE,
    "developer_id": "8879742639795815518",
    "play_app_id": "4974963942177850884",
    "evidence_kind": "native_console_text",
    "observed_upload_key_message": FIRST_UPLOAD_MESSAGE,
    "upload_key_certificate_registered": False,
    "read_only": True,
    "app_signing_changes_performed": False,
    "upload_key_changes_performed": False,
    "aab_uploaded": False,
}


class Blocked(Exception):
    """Only constant, public diagnostics belong here."""


def require(condition, message):
    if not condition:
        raise Blocked(message)


def within(path, parent):
    # Path.is_relative_to is deliberately avoided for clarity on Python 3.9.
    return path == parent or parent in path.parents


def read_evidence(path):
    require(path.is_absolute() and path == RECEIPT,
            "Pass the exact documented public first-upload evidence path.")
    require(path.resolve(strict=True) == path,
            "Public first-upload evidence must not use symlinks.")
    fd = os.open(str(path), os.O_RDONLY | os.O_NOFOLLOW)
    with os.fdopen(fd, "rb") as stream:
        info = os.fstat(stream.fileno())
        require(stat.S_ISREG(info.st_mode) and info.st_size <= 65536,
                "Public first-upload evidence must be a small regular JSON file.")
        receipt = json.loads(stream.read(65537).decode("utf-8-sig"))
    require(isinstance(receipt, dict), "Public first-upload evidence must be an object.")
    for field, expected in REQUIRED_EVIDENCE.items():
        actual = receipt.get(field)
        require(type(actual) is type(expected) and actual == expected,
                "Public first-upload evidence identity or eligibility check failed.")
    url = urlsplit(receipt.get("console_url", ""))
    require(url.scheme == "https" and url.netloc == "play.google.com" and
            re.fullmatch(r"/console/u/\d+/developers/8879742639795815518/"
                         r"app/4974963942177850884/keymanagement", url.path) is not None,
            "Public evidence must identify the exact Play Console keymanagement route.")
    app_signing = receipt.get("app_signing_key")
    require(isinstance(app_signing, dict) and app_signing.get("manager") == "Google Play"
            and app_signing.get("status") == "In use",
            "Public evidence must show the Google-managed app signing key in use.")
    require(isinstance(receipt.get("actor"), str) and bool(receipt["actor"].strip()) and
            isinstance(receipt.get("checked_at"), str) and bool(receipt["checked_at"].strip()),
            "Public Console evidence must include its actor and observation time.")
    # No fingerprint may coexist with the claimed absent upload certificate.
    for field in ("upload_key_certificate_sha256", "upload_key_certificate_sha1",
                  "upload_key_certificate", "uploadCertificateSha256", "uploadCertificateSha1",
                  "uploadKeyCertificateSha256", "uploadKeyCertificateSha1",
                  "uploadCertificate", "uploadKeyCertificate"):
        require(receipt.get(field) in (None, "", {}, []),
                "Public evidence contains an upload certificate; creation refused.")
    return receipt


def validate_paths(output, jdk):
    require(output.is_absolute() and jdk.is_absolute(),
            "Output directory and JDK must be explicit absolute paths.")
    require(not os.path.lexists(str(output)),
            "Output already exists. Preserve it and inspect privately; never blindly retry.")
    parent = output.parent.resolve(strict=True)
    require(parent == output.parent,
            "Output parent must exist and must not contain symlinks or dot components.")
    home = Path.home().resolve(strict=True)
    signing_base = home / "MulticastSigning" / PACKAGE
    require(str(home).startswith("/Users/") and within(parent, signing_base),
            "Output must be beneath the local user's MulticastSigning/package directory.")
    require(not within(output, ROOT) and not within(ROOT, output),
            "Signing material must stay outside the project.")
    require(not any(part in ("Library", "Mobile Documents", "CloudStorage", "Dropbox",
                             "OneDrive", "Google Drive", "GoogleDrive", "iCloud Drive")
                    for part in output.parts),
            "Signing output must not be in a known sync location.")
    for folder in (home / "MulticastSigning", signing_base, parent):
        info = folder.stat()
        require(folder.resolve(strict=True) == folder and stat.S_ISDIR(info.st_mode)
                and info.st_uid == os.getuid() and stat.S_IMODE(info.st_mode) == 0o700,
                "Signing parent directories must be owned by this user with mode 700.")
    # Intermediate parents (if any) must also be private, real directories.
    for folder in parent.parents:
        if within(folder, signing_base):
            info = folder.stat()
            require(info.st_uid == os.getuid() and stat.S_IMODE(info.st_mode) == 0o700,
                    "Every signing parent must be private and owned by this user.")
    selected = jdk.resolve(strict=True)
    require(not within(selected, ROOT), "Select an installed JDK outside the project.")
    keytool = selected / "bin/keytool"
    require(keytool.is_file() and os.access(str(keytool), os.X_OK),
            "Selected installed JDK has no executable bin/keytool.")
    return keytool


def write_exclusive(path, payload):
    fd = os.open(str(path), os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as stream:
        os.fchmod(stream.fileno(), 0o600)
        json.dump(payload, stream, indent=2)
        stream.write("\n")
        stream.flush()
        os.fsync(stream.fileno())


def sync_directory(path):
    fd = os.open(str(path), os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def run_keytool(keytool, arguments, child_env):
    # Never use a shell, inherited JVM options, raw output logging or raw exceptions.
    try:
        result = subprocess.run(
            [str(keytool), "-J-Duser.language=en", "-J-Duser.country=US"] + arguments,
            env=child_env, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
            stderr=subprocess.PIPE, timeout=120, check=False,
        )
    except subprocess.TimeoutExpired:
        raise Blocked("Keytool timed out. Preserve the output directory; do not retry creation.") from None
    except OSError:
        raise Blocked("Keytool could not run. Preserve the output directory for private inspection.") from None
    require(result.returncode == 0,
            "Keytool failed; raw output suppressed. Preserve all partial files for private inspection.")
    return result.stdout


def certificate_der(data):
    certificates = re.findall(
        rb"-----BEGIN CERTIFICATE-----\s*(.*?)\s*-----END CERTIFICATE-----", data, re.S
    )
    require(len(certificates) == 1, "Expected exactly one exported upload certificate.")
    der = base64.b64decode(re.sub(rb"\s", b"", certificates[0]), validate=True)
    # The DER must be one complete SEQUENCE, rather than arbitrary PEM payload.
    require(len(der) > 4 and der[0] == 0x30, "Exported certificate DER is invalid.")
    size = der[1]
    header = 2
    if size & 0x80:
        count = size & 0x7f
        require(1 <= count <= 4 and len(der) >= 2 + count,
                "Exported certificate DER length is invalid.")
        size = int.from_bytes(der[2:2 + count], "big")
        header += count
    require(header + size == len(der), "Exported certificate DER is truncated or has trailing data.")
    return der


def create(args):
    require(sys.version_info >= (3, 9), "Python 3.9 or newer is required.")
    require(platform.system() == "Darwin", "Key creation is supported only on the owner's Mac.")
    require(args.create_first_upload_key, "Explicit --create-first-upload-key is required.")
    evidence_path = Path(args.first_upload_evidence)
    read_evidence(evidence_path)
    output = Path(args.output_dir)
    keytool = validate_paths(output, Path(args.jdk))
    # All read-only gates pass before the first private filesystem mutation.
    os.umask(0o077)
    output.mkdir(mode=0o700, parents=False, exist_ok=False)
    os.chmod(str(output), 0o700)
    keystore = output / "upload.jks"
    secret_ref = output / "signing.secret.json"
    secret = {
        "package": PACKAGE,
        "keystore": str(keystore),
        "alias": ALIAS,
        "storePassword": secrets.token_urlsafe(48),
        "keyPassword": secrets.token_urlsafe(48),
        # certSha256 deliberately absent: incomplete files fail builder checks.
    }
    write_exclusive(secret_ref, secret)
    sync_directory(output)
    sync_directory(output.parent)
    # Minimal environment prevents JAVA_TOOL_OPTIONS / JDK_JAVA_OPTIONS injection.
    child_env = {
        "PATH": "/usr/bin:/bin",
        "HOME": str(Path.home()),
        "LANG": "C",
        "LC_ALL": "C",
        "FIRST_UPLOAD_STORE_PASSWORD": secret["storePassword"],
        "FIRST_UPLOAD_KEY_PASSWORD": secret["keyPassword"],
    }
    common = ["-keystore", str(keystore), "-storetype", "JKS", "-alias", ALIAS,
              "-storepass:env", "FIRST_UPLOAD_STORE_PASSWORD"]
    run_keytool(keytool, ["-genkeypair"] + common + [
        "-keypass:env", "FIRST_UPLOAD_KEY_PASSWORD", "-keyalg", "RSA", "-keysize", "2048",
        "-sigalg", "SHA256withRSA", "-validity", "10000", "-dname",
        "CN=Multicast Games Limited, O=Multicast Games Limited, C=CY", "-noprompt",
    ], child_env)
    info = keystore.lstat()
    require(stat.S_ISREG(info.st_mode) and info.st_uid == os.getuid(),
            "Generated keystore must be a regular file owned by this user.")
    os.chmod(str(keystore), 0o600)
    exported = run_keytool(keytool, ["-exportcert", "-rfc"] + common, child_env)
    der = certificate_der(exported)
    listing = run_keytool(keytool, ["-list", "-v"] + common, child_env)
    require(re.search(rb"(?m)^Alias name: upload\r?$", listing) is not None and
            re.search(rb"(?m)^Entry type: PrivateKeyEntry\r?$", listing) is not None,
            "Generated alias did not authenticate as the expected PrivateKeyEntry.")
    listed_der = certificate_der(
        run_keytool(keytool, ["-list", "-rfc"] + common, child_env)
    )
    require(listed_der == der, "Export and list returned different certificates.")
    with keystore.open("rb") as stream:
        os.fsync(stream.fileno())
    sha256 = hashlib.sha256(der).hexdigest().upper()
    sha1_digest = hashlib.sha1(der).digest()
    # Recheck the actual readback before marking the private credentials complete.
    read_evidence(evidence_path)
    secret["certSha256"] = sha256
    completed = output / "signing.completed.secret.json"
    write_exclusive(completed, secret)
    # Only our own incomplete secret is replaced. Existing directories are refused.
    os.replace(str(completed), str(secret_ref))
    sync_directory(output)
    return {
        "keystore": str(keystore),
        "secretRef": str(secret_ref),
        "alias": ALIAS,
        "certificateSha256": sha256,
        "certificateSha1": sha1_digest.hex().upper(),
        "metaUploadKeyHash": base64.b64encode(sha1_digest).decode("ascii"),
        "status": "first_upload_key_verified",
        "first_upload_evidence_ref": str(evidence_path),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--create-first-upload-key", action="store_true")
    parser.add_argument("--output-dir", required=True,
                        help="Fresh directory beneath ~/MulticastSigning/" + PACKAGE)
    parser.add_argument("--jdk", required=True, help="Absolute installed JDK directory")
    parser.add_argument("--first-upload-evidence", required=True,
                        help="Absolute path to the documented actual public Console receipt")
    args = parser.parse_args()
    try:
        metadata = create(args)
    except Blocked as error:
        print("Blocked: " + str(error), file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        print("Interrupted. Preserve any output directory; inspect privately before any further action.",
              file=sys.stderr)
        return 130
    except Exception:
        # Includes filesystem, JSON, base64 and decoding failures. No raw exceptions.
        print("Blocked: local validation or creation failed; details suppressed. Preserve any output "
              "directory and inspect privately. Do not blindly retry.", file=sys.stderr)
        return 1
    print(json.dumps(metadata, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
