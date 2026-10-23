#!/usr/bin/env bash
# На хабе: прогнать вариант в headless-Chrome на Mac с автоигроком и вывести журнал теста (#testlog).
#   Tools/Playable/runlog.sh long 100 20000 casual     # вариант, ускорение, виртуальное время в мс, бот (fast|casual)
set -euo pipefail
cd "$(dirname "$0")/../.."
V="$1"; SP="$2"; MS="$3"; BOT="${4:-fast}"
F="release/playables/cat-miners-$V.html"
H=$(md5sum "$F" | cut -d' ' -f1)
for i in $(seq 1 90); do [ "$(on-remote md5 -q "$F" 2>/dev/null)" = "$H" ] && break; sleep 1; done
on-remote bash -c 'CH="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"; P="$PWD/build/chrome-shots/log-'"$V-$BOT"'"; mkdir -p "$P"; F="$PWD/'"$F"'";
  "$CH" --headless=new --mute-audio --no-first-run --user-data-dir="$P" --window-size=540,1170 --virtual-time-budget='"$MS"' --dump-dom "file://${F// /%20}#test='"$SP"'&vh=1170&bot='"$BOT"'" 2>/dev/null' \
  | python3 -c 'import sys,re,html; d=sys.stdin.read(); m=re.search(r"<pre id=\"testlog\" style=\"[^\"]*\">(.*?)</pre>", d, re.S); print(html.unescape(m.group(1)) if m else "(нет журнала)")'
