#!/usr/bin/env bash
# Unity без GUI на macOS для Cats Cute Miner: тесты EditMode/PlayMode и методы Editor в изолированной копии build/ci
# (Assets/Packages/ProjectSettings зеркалятся из проекта, Library своя), поэтому открытый в Unity проект не мешает.
# Аналог Tools/unity.ps1 -Target Ci для Mac.
#
#   Tools/unity-mac.sh PlayMode "OutpostDressingEvidenceTests"     # фильтр -testFilter (регулярное выражение Unity)
#   Tools/unity-mac.sh EditMode "MineDressingTests"
#   Tools/unity-mac.sh Method CatMiners.Editor.BuildMac.BuildDevelopment
#   Tools/unity-mac.sh Compile                                      # только импорт и компиляция, без тестов
#
# Логи и результаты: build/ci-logs/ (не синхронизируются), отчёты и снимки тестов: Docs/Reports/ проекта.
# Первая копия Library берётся из проекта (rsync), дальше копия живёт своей жизнью.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
MODE="${1:-PlayMode}"
ARG="${2:-}"
VERSION="$(sed -n 's/^m_EditorVersion: *//p' "$ROOT/ProjectSettings/ProjectVersion.txt")"
UNITY="${UNITY_EXE:-/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity}"
CI_NAME="${CATMINERS_CI_WORKSPACE:-ci}"
[[ "$CI_NAME" =~ ^[a-z0-9-]+$ ]] || { echo "invalid CI workspace" >&2; exit 2; }
CI="$ROOT/build/$CI_NAME"
LOGS="$ROOT/build/ci-logs"
mkdir -p "$CI" "$LOGS" "$ROOT/Docs/Reports"

rsync -a --delete "$ROOT/Assets" "$ROOT/Packages" "$ROOT/ProjectSettings" "$CI/"
if [ ! -d "$CI/Library" ] && [ -d "$ROOT/Library" ]; then rsync -a --exclude '*.lock' "$ROOT/Library" "$CI/"; fi

STAMP="$(date +%Y%m%d-%H%M%S)"
LOG="$LOGS/$MODE-$STAMP.log"
ARGS=(-batchmode -projectPath "$CI" -logFile "$LOG" -catminersReports "$ROOT/Docs/Reports")
# Exercise platform-gated code in the same isolated test checkout when requested.
if [ -n "${CATMINERS_BUILD_TARGET:-}" ]; then
  case "$CATMINERS_BUILD_TARGET" in
    Android|WebGL|StandaloneOSX) ARGS+=(-buildTarget "$CATMINERS_BUILD_TARGET") ;;
    *) echo "invalid Unity build target" >&2; exit 2 ;;
  esac
fi
case "$MODE" in
  PlayMode|EditMode)
    XML="$LOGS/$MODE-$STAMP.xml"
    ARGS+=(-runTests -testPlatform "$MODE" -testResults "$XML")
    [ "$MODE" = EditMode ] && ARGS+=(-nographics)
    [ -n "$ARG" ] && ARGS+=(-testFilter "$ARG")
    ;;
  Method)
    ARGS+=(-quit -executeMethod "$ARG")
    ;;
  Compile)
    ARGS+=(-quit -nographics)
    ;;
  *) echo "unknown mode $MODE" >&2; exit 2 ;;
esac

echo "[unity-mac] $MODE ${ARG} log=$LOG"
set +e
"$UNITY" "${ARGS[@]}"
CODE=$?
set -e
echo "[unity-mac] exit=$CODE"
grep -E 'error CS[0-9]+' "$LOG" | sort -u | head -40 || true
grep -E 'warning CS[0-9]+' "$LOG" | grep -E 'Assets/Game' | sort -u | head -20 || true
if [ -n "${XML:-}" ] && [ -f "$XML" ]; then
  head -c 600 "$XML" | grep -o '<test-run[^>]*>' || true
  grep -o '<test-case [^>]*result="Failed"[^>]*>' "$XML" | sed 's/.*fullname="\([^"]*\)".*/FAILED \1/' | head -40 || true
fi
exit $CODE
