#!/usr/bin/env python3
"""Restore the pinned Meta SDK, including files left as Git LFS pointers.

Run from the project root: python3 Tools/ReleaseSdk/restore-meta-binaries.py
Only binaries listed in meta-binary-hashes.json are installed. Existing real
binaries must match; .meta files and local source patches are preserved.
"""

import hashlib
import io
import json
from pathlib import Path
import shutil
import tarfile
import tempfile
import urllib.request
import zipfile


ROOT = Path(__file__).resolve().parents[2]
MANIFEST = Path(__file__).with_name("meta-binary-hashes.json")
LFS_HEADER = b"version https://git-lfs.github.com/spec/v1\n"


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def restore():
    expected = json.loads(MANIFEST.read_text())
    # Validate every existing file before making any changes.
    for name, digest in expected["files"].items():
        relative = Path(name)
        if not name.startswith("Assets/FacebookSDK/") or ".." in relative.parts:
            raise ValueError("Invalid SDK delivery path: " + name)
        target = ROOT / relative
        if not target.with_name(target.name + ".meta").is_file():
            raise ValueError("Missing SDK metadata: " + name)
        if not target.is_file():
            continue
        data = target.read_bytes()
        if data.startswith(LFS_HEADER):
            if ("oid sha256:" + digest) not in data.decode("ascii").splitlines():
                raise ValueError("LFS pointer differs from pinned SDK: " + name)
        elif sha256(data) != digest:
            raise ValueError("Preserving different existing SDK binary: " + name)

    staging = ROOT / "Temp"
    staging.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="meta-sdk-", dir=staging) as folder:
        archive_path = Path(folder) / "sdk.zip"
        request = urllib.request.Request(expected["source"], headers={"User-Agent": "CatsCuteMiner-SDK-Restore"})
        with urllib.request.urlopen(request, timeout=60) as response, archive_path.open("wb") as output:
            shutil.copyfileobj(response, output)
        if sha256(archive_path.read_bytes()) != expected["zip_sha256"]:
            raise ValueError("Official Meta archive SHA-256 mismatch")

        payloads = {}
        with zipfile.ZipFile(archive_path) as archive:
            packages = [n for n in archive.namelist() if n.endswith(".unitypackage")]
            if len(packages) != 1:
                raise ValueError("Expected one Unity package in the official archive")
            with tarfile.open(fileobj=io.BytesIO(archive.read(packages[0])), mode="r:gz") as package:
                for member in package.getmembers():
                    if not member.isfile() or not member.name.endswith("/pathname"):
                        continue
                    name = package.extractfile(member).read().decode("utf-8").strip()
                    if name not in expected["files"]:
                        continue
                    data = package.extractfile(member.name.rsplit("/", 1)[0] + "/asset").read()
                    if sha256(data) != expected["files"][name]:
                        raise ValueError("SDK binary SHA-256 mismatch: " + name)
                    payloads[name] = data

        if set(payloads) != set(expected["files"]):
            raise ValueError("Official archive is missing pinned SDK binaries")
        for name, data in payloads.items():
            target = ROOT / name
            if target.is_file() and sha256(target.read_bytes()) == expected["files"][name]:
                continue
            target.write_bytes(data)
        print("Meta SDK 18.1.1: {} binaries verified and restored".format(len(payloads)))


if __name__ == "__main__":
    restore()
