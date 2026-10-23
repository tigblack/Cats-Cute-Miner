#!/usr/bin/env python3
"""Записывает Adjust event tokens в конфиг игры (adjustEvents по имени события).

  fill_adjust_tokens.py <GameServiceConfig.json> <tokens.json> [--force]

tokens.json: {"имя_события": "токен", ...}, снятое из кабинета Adjust (приложение -> Events).
Токен события `purchase` идёт в adjustPurchaseToken. Уже стоящий другой токен не затирается без --force.
Печатает, что записано, какие события остались без токена (они не отправляются) и какие имена из
tokens.json неизвестны конфигу. Токены не секретны, но принадлежат приложению этой игры."""
import argparse
import json
import re
import sys
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("config")
    parser.add_argument("tokens")
    parser.add_argument("--force", action="store_true", help="overwrite a different existing token")
    args = parser.parse_args()

    config_path = Path(args.config)
    config = json.loads(config_path.read_text(encoding="utf-8"))
    tokens = json.loads(Path(args.tokens).read_text(encoding="utf-8"))
    if not isinstance(tokens, dict) or not all(isinstance(k, str) and isinstance(v, str) for k, v in tokens.items()):
        sys.exit('tokens.json must look like {"event_name": "token"}')
    suspicious = [k for k, v in tokens.items() if not re.fullmatch(r"[a-z0-9]{4,12}", v)]
    if suspicious:
        sys.exit("Unexpected token format (Adjust tokens are short lowercase alphanumerics): " + ", ".join(suspicious))

    events = config.pop("adjustEvents", None) or []
    by_name = {e.get("name"): e for e in events if isinstance(e, dict)}
    written, same, conflicts = [], [], []
    for name, token in tokens.items():
        if name == "purchase":
            current = config.get("adjustPurchaseToken") or ""
            if current == token:
                same.append(name)
            elif current and not args.force:
                conflicts.append(name)
            else:
                config["adjustPurchaseToken"] = token
                written.append(name)
            continue
        entry = by_name.get(name)
        if entry is None:
            continue
        current = entry.get("token") or ""
        if current == token:
            same.append(name)
        elif current and not args.force:
            conflicts.append(name)
        else:
            entry["token"] = token
            written.append(name)
    missing = [n for n, e in by_name.items() if not e.get("token")]
    unknown = [n for n in tokens if n != "purchase" and n not in by_name]

    body = json.dumps(config, ensure_ascii=False, indent=2)
    items = ",\n".join("    " + json.dumps(e, ensure_ascii=False) for e in events)
    config_path.write_text(body[:-2].rstrip() + ',\n  "adjustEvents": [\n' + items + "\n  ]\n}\n", encoding="utf-8")

    print("written:", len(written), "| unchanged:", len(same), "| conflicts (kept):", len(conflicts))
    if conflicts:
        print("  conflicting tokens kept, use --force to overwrite:", ", ".join(conflicts))
    print("events still without token (not sent):", len(missing))
    if missing:
        print("  " + ", ".join(missing))
    if unknown:
        print("names in tokens.json that the config does not list:", ", ".join(unknown))


if __name__ == "__main__":
    main()
