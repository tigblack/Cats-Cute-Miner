#!/usr/bin/env python3
"""Basic CSV preflight and one-shot Google Play Data safety submission."""

import argparse
import csv
import hashlib
import http.client
import io
import json
import os
from pathlib import Path
import re
import sys
from datetime import datetime, timezone
import urllib.error
import urllib.request

COLUMNS = (
    "Question ID (machine readable)", "Response ID (machine readable)",
    "Response value", "Answer requirement", "Human-friendly question label",
)
COLLECTS = "PSL_DATA_COLLECTION_COLLECTS_PERSONAL_DATA"
ENCRYPTED = "PSL_DATA_COLLECTION_ENCRYPTED_IN_TRANSIT"
ACCOUNTS = "PSL_SUPPORTED_ACCOUNT_CREATION_METHODS"
USAGE = "PSL_DATA_USAGE_RESPONSES:"


def validate_csv(raw):
    """Return exact decoded CSV and basic preflight errors; never infer answers."""
    content = raw.decode("utf-8")
    reader = csv.DictReader(io.StringIO(content.lstrip("\ufeff"), newline=""), strict=True)
    errors = []
    if not reader.fieldnames or any(c not in reader.fieldnames for c in COLUMNS):
        return content, ["CSV is missing required columns"]
    if len(set(reader.fieldnames)) != len(reader.fieldnames):
        return content, ["CSV has duplicate columns"]
    groups = {}
    cells = set()
    for row in reader:
        if None in row or any(row[c] is None for c in COLUMNS):
            errors.append("CSV row has the wrong number of columns")
            continue
        question, response, value, requirement, _ = (row[c].strip() for c in COLUMNS)
        if not question:
            errors.append("CSV row has no question ID")
            continue
        if (question, response) in cells:
            errors.append(f"Duplicate question/response cell: {question}")
        cells.add((question, response))
        if value not in ("", "TRUE", "FALSE"):
            errors.append(f"Expected TRUE, FALSE or blank: {question}")
        groups.setdefault(question, []).append((response, value, requirement))

    def boolean(question):
        rows = groups.get(question, [])
        if len(rows) != 1 or rows[0][0] or rows[0][1] not in ("TRUE", "FALSE"):
            errors.append(f"Required boolean missing: {question}")
            return None
        return rows[0][1] == "TRUE"

    collects = boolean(COLLECTS)
    selected = {response for question, rows in groups.items()
                if question.startswith("PSL_DATA_TYPES_")
                for response, value, _ in rows if response and value == "TRUE"}
    if collects:
        boolean(ENCRYPTED)
        if not selected:
            errors.append("Select at least one data type")
        if not any(value == "TRUE" for _, value, _ in groups.get(ACCOUNTS, [])):
            errors.append(f"Missing current-schema group or selection: {ACCOUNTS}")
        for data_type in sorted(selected):
            question = f"{USAGE}{data_type}:DATA_USAGE_USER_CONTROL"
            if sum(value == "TRUE" for _, value, _ in groups.get(question, [])) != 1:
                errors.append(f"Select exactly one user-control answer: {question}")
    # shortcut: only known usage dependencies are modeled; use Console for evolving schema checks.
    for question, rows in groups.items():
        active = True
        if question.startswith(USAGE):
            parts = question.split(":")
            active = bool(collects and len(parts) == 3 and parts[1] in selected)
        if not active:
            continue
        if any(requirement == "REQUIRED" and not value for _, value, requirement in rows):
            errors.append(f"Required answer missing: {question}")
        if any(response and requirement == "REQUIRED" for response, _, requirement in rows):
            if not any(value == "TRUE" for _, value, _ in rows):
                errors.append(f"Required selection missing: {question}")
        if any(requirement == "SINGLE_CHOICE" for _, _, requirement in rows):
            count = sum(value == "TRUE" for _, value, _ in rows)
            if count > 1:
                errors.append(f"Multiple answers for single choice: {question}")
    return content, errors


class RejectRedirects(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def submit(package, csv_path, token_path, receipt_path):
    if not re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z][A-Za-z0-9_]*)+", package):
        raise ValueError("Invalid Android package name")
    csv_path, token_path, receipt_path = map(Path, (csv_path, token_path, receipt_path))
    if receipt_path.resolve() in (csv_path.resolve(), token_path.resolve()):
        raise ValueError("Receipt must not be an input file")
    raw = csv_path.read_bytes()
    content, errors = validate_csv(raw)
    if errors:
        raise ValueError("CSV preflight failed:\n" + "\n".join(errors))
    # Reserve before reading credentials; exclusive creation also rejects existing symlinks.
    receipt = {
        "package": package, "csvSha256": hashlib.sha256(raw).hexdigest(),
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "status": "pending", "apiAccepted": None, "consoleReadbackVerified": False,
    }
    with receipt_path.open("x", encoding="utf-8", newline="\n") as output:
        def save():
            output.seek(0)
            json.dump(receipt, output, indent=2)
            output.write("\n")
            output.truncate()
            output.flush()
            os.fsync(output.fileno())

        save()
        try:
            token = token_path.read_text(encoding="utf-8").strip()
            if not token or not re.fullmatch(r"[\x21-\x7e]+", token):
                raise ValueError("Invalid OAuth token file")
        except (OSError, UnicodeError, ValueError):
            receipt.update(status="credential_error", apiAccepted=False)
            save()
            raise ValueError("Cannot read a valid OAuth token from the supplied file") from None
        request = urllib.request.Request(
            f"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/{package}/dataSafety",
            data=json.dumps({"safetyLabels": content}, ensure_ascii=False).encode("utf-8"),
            headers={"Authorization": f"Bearer {token}", "Content-Type": "application/json; charset=utf-8"},
            method="POST",
        )
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), RejectRedirects())
        try:
            with opener.open(request, timeout=30) as response:
                status = response.status
            receipt.update(httpStatus=status, apiAccepted=200 <= status < 300,
                           status="accepted" if 200 <= status < 300 else "http_error")
        except urllib.error.HTTPError as error:
            receipt.update(status="http_error", httpStatus=error.code, apiAccepted=False)
            error.close()
        except (OSError, urllib.error.URLError, http.client.HTTPException):
            receipt.update(status="transport_error", apiAccepted=None)
        save()
    return receipt["apiAccepted"] is True


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    validate = commands.add_parser("validate", help="Offline basic preflight, not a full Google schema validator")
    validate.add_argument("--csv", required=True, type=Path)
    send = commands.add_parser("submit", help="Submit once using a supplied androidpublisher OAuth token")
    send.add_argument("--package", required=True)
    send.add_argument("--csv", required=True, type=Path)
    send.add_argument("--access-token-file", required=True, type=Path)
    send.add_argument("--receipt", required=True, type=Path)
    args = parser.parse_args(argv)
    try:
        if args.command == "validate":
            _, errors = validate_csv(args.csv.read_bytes())
            if errors:
                raise ValueError("CSV preflight failed:\n" + "\n".join(errors))
            print("Basic CSV preflight passed; Console schema/readback still requires checking.")
            return 0
        accepted = submit(args.package, args.csv, args.access_token_file, args.receipt)
        print("API accepted; verify Console readback separately." if accepted else
              "Submission failed or acceptance is unknown; inspect receipt. No retry performed.")
        return 0 if accepted else 1
    except (OSError, UnicodeError, csv.Error):
        print("Cannot process files or CSV; no retry performed.", file=sys.stderr)
    except ValueError as error:
        print(str(error), file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())
