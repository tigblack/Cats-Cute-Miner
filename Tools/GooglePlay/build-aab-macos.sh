#!/bin/sh
# Existing signing only. Never pass passwords on the command line.
set -eu
set +x
umask 077
exec python3 "$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)/google_play_macos.py" build "$@"
