#!/usr/bin/env bash
# Быстрая проверка CatMiners.Core без Unity на macOS — аналог run.ps1: сборка под netstandard2.1 / C# 9 и прогон EditMode-тестов
# через dotnet test. dotnet — из PATH или встроенный в установленный Unity (версия из ProjectVersion.txt).
# Файлы *.csproj не синхронизируются Mutagen, поэтому они создаются здесь из шаблонов.
#
#   Tools/CoreTests/run.sh                                        # все тесты
#   Tools/CoreTests/run.sh -f 'FullyQualifiedName~Outpost'        # фильтр dotnet test
#   Tools/CoreTests/run.sh -f '...' -i progression                # свои obj/bin (obj/CoreTests-<имя>) для параллельных прогонов
#   Tools/CoreTests/run.sh -d Draft.cs                            # черновик теста из Tools/CoreTests/Drafts
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
VERSION="$(sed -n 's/^m_EditorVersion: *//p' "$ROOT/ProjectSettings/ProjectVersion.txt")"
DOTNET="${DOTNET_EXE:-$(command -v dotnet || true)}"
[ -n "$DOTNET" ] || DOTNET="/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet"

# Копируем только при отличии: параллельные прогоны не перезаписывают файл, который читает другой.
sync_template() { cmp -s "$1" "$2" 2>/dev/null || cp "$1" "$2"; }
sync_template "$HERE/CoreLib/CoreLib.csproj.template" "$HERE/CoreLib/CoreLib.csproj"
sync_template "$HERE/Tests/Tests.csproj.template" "$HERE/Tests/Tests.csproj"

FILTER="" INSTANCE="" DRAFT=""
while [ $# -gt 0 ]; do
  case "$1" in
    -f|--filter) FILTER="$2"; shift 2 ;;
    -i|--instance) INSTANCE="$2"; shift 2 ;;
    -d|--draft) DRAFT="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
ARGS=(test "$HERE/Tests/Tests.csproj" --nologo -v q --logger "console;verbosity=normal")
[ -n "$FILTER" ] && ARGS+=(--filter "$FILTER")
[ -n "$INSTANCE" ] && ARGS+=("-p:CoreTestsInstance=$INSTANCE")
[ -n "$DRAFT" ] && ARGS+=("-p:CoreTestsDraft=$DRAFT")
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
set +e
"$DOTNET" "${ARGS[@]}" 2>&1 | grep -v -E '^\s*$|warning NU|Determining projects|Restored '
CODE=${PIPESTATUS[0]}
exit "$CODE"
