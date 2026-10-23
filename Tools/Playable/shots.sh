#!/usr/bin/env bash
# Снимки playable headless-Chrome на Mac (тестовый режим #test=скорость: автоигрок, ускоренное время).
#   Tools/Playable/shots.sh short 1 540 1170 1500 3000 9000   # вариант, ускорение, размер окна, моменты в мс
#   BOT=none|casual — без автоигрока / «обычный» игрок (по умолчанию быстрый)
# PNG — release/playables/shots/<вариант>-<мс>x<ускорение>-<W>x<H>.png (не синхронизируются с хабом: on-remote --fetch).
# Окно уже 500 px headless не открывает: телефон снимается как 540×1170 (×2 = 1080×2340).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
V="${1:-short}"; SPEED="${2:-1}"; W="${3:-540}"; H="${4:-1170}"
shift 4 || true
CH="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
OUT="$ROOT/release/playables/shots"
mkdir -p "$OUT"
FILE="$ROOT/release/playables/cat-miners-$V.html"
URL="file://${FILE// /%20}#test=$SPEED&vh=$H${BOT:+&bot=$BOT}"
for MS in "${@:-3000}"; do
  PNG="$OUT/$V${BOT:+-$BOT}-${MS}x${SPEED}-${W}x${H}.png"
  # свой профиль на каждый снимок: снимки можно запускать параллельно
  PROFILE="$ROOT/build/chrome-shots/$V-$MS-$SPEED-$W${BOT:+-$BOT}"
  mkdir -p "$PROFILE"
  "$CH" --headless=new --hide-scrollbars --mute-audio --no-first-run --no-default-browser-check \
    --user-data-dir="$PROFILE" --window-size="$W,$H" --force-device-scale-factor=2 \
    --virtual-time-budget="$MS" --screenshot="$PNG" "$URL" >/dev/null 2>&1 || true
  echo "$PNG"
done
