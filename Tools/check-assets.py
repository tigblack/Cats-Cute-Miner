#!/usr/bin/env python3
"""Fail if Unity assets contain unresolved Git LFS pointers."""

from pathlib import Path
import sys


def main():
    root = Path(__file__).resolve().parents[1]
    pointers = []
    for path in (root / "Assets").rglob("*"):
        if not path.is_file() or path.suffix == ".meta":
            continue
        with path.open("rb") as asset:
            if asset.read(43).startswith(b"version https://git-lfs.github.com/spec/v1"):
                pointers.append(path.relative_to(root).as_posix())
    if pointers:
        print("Unresolved Git LFS assets: {}".format(len(pointers)))
        for path in sorted(pointers):
            print("  " + path)
        print("Use a Git clone with Git LFS installed, then run git lfs pull.")
        print("A source ZIP can contain pointers instead of images, audio, fonts and SDK binaries.")
        return 1
    print("Unity assets: no unresolved Git LFS pointers.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
