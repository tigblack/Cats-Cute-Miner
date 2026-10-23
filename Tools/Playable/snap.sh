#!/usr/bin/env bash
# На хабе: дождаться синхронизации сборки на Mac, снять кадры (shots.sh на Mac), забрать PNG и собрать лист.
#   Tools/Playable/snap.sh medium 1 2500 7000 14000      # вариант, ускорение, моменты в мс
#   BOT=none|casual — без автоигрока / «обычный» игрок; W/H — размер окна (по умолчанию 540×1170)
set -euo pipefail
cd "$(dirname "$0")/../.."
V="$1"; SP="$2"; shift 2
W="${W:-540}"; H="${H:-1170}"
F="release/playables/cat-miners-$V.html"
SUM=$(md5sum "$F" | cut -d' ' -f1)
for i in $(seq 1 90); do [ "$(on-remote md5 -q "$F" 2>/dev/null)" = "$SUM" ] && break; sleep 1; done
on-remote env BOT="${BOT:-}" bash Tools/Playable/shots.sh "$V" "$SP" "$W" "$H" "$@" >/dev/null
files=()
for ms in "$@"; do
  f="release/playables/shots/$V${BOT:+-$BOT}-${ms}x$SP-${W}x${H}.png"
  rm -f "$f"; on-remote --fetch "$f" >/dev/null 2>&1 || true
  files+=("$f")
done
Tools/.venv/bin/python Tools/Playable/sheet.py "release/playables/shots/sheet-$V${BOT:+-$BOT}-x$SP-${W}x${H}.png" "${files[@]}"
