#!/bin/sh
# Read-only inventory; no Unity, adb server, installer or signing-secret access.
set -eu
set +x
exec python3 "$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)/google_play_macos.py" preflight "$@"
