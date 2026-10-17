#!/usr/bin/env python3
"""Read-only source inventory. Signals never certify initialization or runtime QA.

Uses only the Python standard library. Reports paths/line numbers without source
contents or credential values. Does not execute project code or follow symlinks.
"""

import argparse
import json
import os
from pathlib import Path
import re
import sys


EXCLUDED_DIRS = {
    ".git", ".agents", ".claude", ".codex", ".t3", ".godot", ".gradle",
    ".idea", ".vs", ".vscode", "__pycache__", "node_modules", "library",
    "temp", "obj", "bin",
}
# Apply artifact exclusions only at the application root. Nested Release,
# vendor and android/build directories can contain live integration sources.
ROOT_EXCLUDED_DIRS = {
    "build", "builds", "dist", "logs", "output", "_research", "captures",
    "recordings", "usersettings", "studio-skills", "test", "tests", "testdata",
}
EXCLUDED_PATHS = {"release/google-play"}
TEXT_EXTENSIONS = {
    ".cs", ".gd", ".java", ".kt", ".kts", ".gradle", ".xml", ".json",
    ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".html", ".cfg",
    ".godot", ".asset", ".prefab", ".unity", ".asmdef", ".properties",
    ".toml", ".yaml", ".yml", ".txt", ".meta",
}
CODE_EXTENSIONS = {".cs", ".gd", ".java", ".kt", ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".html"}
MAX_FILE_BYTES = 2 * 1024 * 1024
MAX_EVIDENCE = 25


def patterns(**signals):
    return {key: re.compile(value, re.IGNORECASE) for key, value in signals.items()}


PROVIDERS = {
    "appmetrica": patterns(
        sdk=r"io[./]appmetrica|appmetrica[-_.](?:unity|analytics|sdk)|\bAppMetrica\b|YandexMetrica",
        initialization=r"(?:AppMetrica|YandexMetrica)\s*\.\s*(?:Activate|activate)\s*\(",
        custom_events=r"(?:AppMetrica|YandexMetrica)\s*\.\s*(?:ReportEvent|reportEvent)\s*\(",
        ad_revenue=r"(?:AppMetrica|YandexMetrica)\s*\.\s*(?:ReportAdRevenue|reportAdRevenue|ReportAutoCollectedAdRevenue)\s*\(",
        iap_revenue=r"(?:AppMetrica|YandexMetrica)\s*\.\s*(?:ReportRevenue|reportRevenue)\s*\(|com\.multicastgames\.payments\.appmetrica",
    ),
    "adjust": patterns(
        sdk=r"AdjustSdk|com[./]adjust|adjust[-_.](?:unity|android|sdk)|import\s+.*Adjust",
        initialization=r"Adjust\s*\.\s*(?:InitSdk|initSdk|onCreate|start)\s*\(",
        events=r"Adjust\s*\.\s*(?:TrackEvent|trackEvent)\s*\(",
        ad_revenue=r"Adjust\s*\.\s*(?:TrackAdRevenue|trackAdRevenue)\s*\(",
    ),
    "meta": patterns(
        sdk=r"Facebook\.Unity|facebook[-_.](?:android|unity|sdk)|com\.facebook|AppEventsLogger",
        initialization=r"FB\s*\.\s*Init\s*\(|FacebookSdk\s*\.\s*sdkInitialize\s*\(",
        activation=r"FB\s*\.\s*ActivateApp\s*\(|AppEventsLogger\s*\.\s*activateApp\s*\(",
        events=r"FB\s*\.\s*(?:LogAppEvent|LogPurchase)\s*\(|\blog(?:Event|Purchase)\s*\(",
    ),
    "max": patterns(
        sdk=r"\bMaxSdk(?:Callbacks|Base)?\b|com\.applovin|applovin[-_.](?:sdk|max)|AppLovinSdk",
        initialization=r"MaxSdk\s*\.\s*InitializeSdk\s*\(|\binitializeSdk\s*\(",
        rewarded=r"MaxSdk(?:Callbacks)?\s*\.\s*(?:Rewarded|LoadRewardedAd|ShowRewardedAd)|\bMaxRewardedAd\b",
        interstitial=r"MaxSdk(?:Callbacks)?\s*\.\s*(?:Interstitial|LoadInterstitial|ShowInterstitial)|\bMaxInterstitialAd\b",
        banner=r"MaxSdk(?:Callbacks)?\s*\.\s*(?:Banner|CreateBanner|ShowBanner)|\bMaxAdView\b",
        ad_revenue=r"OnAdRevenuePaidEvent|\bMaxAdRevenueListener\b|\bonAdRevenuePaid\s*\(",
    ),
    "iap": patterns(
        sdk=r"com\.unity\.purchasing|UnityEngine\.Purchasing|com\.android\.billingclient|BillingClient|com\.multicastgames\.payments",
        initialization=r"UnityPurchasing\s*\.\s*Initialize\s*\(|\bstartConnection\s*\(|StoreController|IPaymentsService",
        processing=r"ProcessPurchase\s*\(|OnPurchasePending|PurchasesUpdatedListener|onPurchasesUpdated\s*\(|ContinueInterruptedPayments",
        confirmation=r"ConfirmPendingPurchase\s*\(|ConfirmPurchase\s*\(|acknowledgePurchase\s*\(|consumeAsync\s*\(",
    ),
}
CONSENT = patterns(
    integration=r"SetHasUserConsent\s*\(|HasUserConsent\s*\(|IUserConsentService|UserMessagingPlatform|ConsentInformation|setConsent\s*\(",
    forced_grant_review=r"SetHasUserConsent\s*\(\s*true\s*\)|SetAdvertiserTrackingEnabled\s*\(\s*true\s*\)|SetAdvertiserIDCollectionEnabled\s*\(\s*true\s*\)",
)


def add_evidence(signals, name, path, line, kind):
    signal = signals[name]
    signal["match_count"] += 1
    if len(signal["evidence"]) < MAX_EVIDENCE:
        signal["evidence"].append({"path": path, "line": line, "kind": kind})


def empty_signals(regexes):
    return {name: {"match_count": 0, "evidence": []} for name in regexes}


def inventory(root):
    report = {
        "schema_version": 1,
        "project_name": root.name,
        "scope": "source_inventory_only",
        "readiness": "not_assessed",
        "engines": [],
        "android_markers": [],
        "providers": {name: {"runtime": "not_verified", "signals": empty_signals(regexes)} for name, regexes in PROVIDERS.items()},
        "consent": {"runtime": "not_verified", "signals": empty_signals(CONSENT)},
        "coverage": {
            "files_scanned": 0, "unreadable_files": [], "skipped_large_files": [],
            "excluded_dirs": sorted(EXCLUDED_DIRS),
            "excluded_root_dirs": sorted(ROOT_EXCLUDED_DIRS),
            "excluded_paths": sorted(EXCLUDED_PATHS),
        },
        "limitations": [
            "Regex signals include declarations and comments, not proof of execution.",
            "SDK/package declarations and adapters do not prove an application integration.",
            "No binary/cache, dashboard, credential, permission, build or device checks are performed.",
            "Excluded paths, files over 2 MiB, symlinks and remote-only files are not inspected.",
            "not_detected means no source signal in this scan, not confirmed absence.",
        ],
    }
    engines = set()

    def scan_error(error):
        report["coverage"]["unreadable_files"].append({"path": str(error.filename), "error": type(error).__name__})

    for parent, dirs, files in os.walk(root, followlinks=False, onerror=scan_error):
        directory = Path(parent)
        dirs[:] = sorted(
            d for d in dirs
            if d.lower() not in EXCLUDED_DIRS
            and not (directory == root and d.lower() in ROOT_EXCLUDED_DIRS)
            and (directory / d).relative_to(root).as_posix().lower() not in EXCLUDED_PATHS
            and not (directory / d).is_symlink()
        )
        for filename in sorted(files):
            path = directory / filename
            relative = path.relative_to(root).as_posix()
            if path.is_symlink():
                continue
            if filename == "ProjectVersion.txt" and directory.name == "ProjectSettings":
                engines.add("unity")
            if filename == "project.godot":
                engines.add("godot")
            if filename == "package.json":
                engines.add("javascript_package_present")
            if filename in {"AndroidManifest.xml", "export_presets.cfg", "capacitor.config.ts", "capacitor.config.json", "config.xml", "build.gradle", "build.gradle.kts"}:
                report["android_markers"].append(relative)
            if path.suffix.lower() not in TEXT_EXTENSIONS:
                continue
            try:
                if path.stat().st_size > MAX_FILE_BYTES:
                    report["coverage"]["skipped_large_files"].append(relative)
                    continue
                source = path.read_text(encoding="utf-8-sig")
            except (OSError, UnicodeError) as error:
                report["coverage"]["unreadable_files"].append({"path": relative, "error": type(error).__name__})
                continue
            report["coverage"]["files_scanned"] += 1
            kind = "source" if path.suffix.lower() in CODE_EXTENSIONS else "configuration_or_dependency"
            provider_hits = {name for name, regexes in PROVIDERS.items() if regexes["sdk"].search(source)}
            for number, line in enumerate(source.splitlines(), 1):
                for name in provider_hits:
                    for signal, regex in PROVIDERS[name].items():
                        if regex.search(line):
                            add_evidence(report["providers"][name]["signals"], signal, relative, number, kind)
                for signal, regex in CONSENT.items():
                    if regex.search(line):
                        add_evidence(report["consent"]["signals"], signal, relative, number, kind)

    report["engines"] = sorted(engines)
    for provider in report["providers"].values():
        provider["source_status"] = "signals_found" if any(s["match_count"] for s in provider["signals"].values()) else "not_detected"
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("project", type=Path, help="Selected application root, not the whole monorepo")
    parser.add_argument("--output", type=Path, help="Write JSON here instead of stdout")
    args = parser.parse_args()
    root = args.project.expanduser().resolve()
    if not root.is_dir():
        parser.error("Project path must be an existing directory")
    # Prevent accidentally overwriting sources with an inventory report.
    if args.output and args.output.exists():
        try:
            previous = json.loads(args.output.read_text(encoding="utf-8"))
            if previous.get("scope") != "source_inventory_only":
                parser.error("Existing output is not an inventory report; choose another path")
        except (OSError, UnicodeError, ValueError, AttributeError):
            parser.error("Existing output is not a readable inventory report; choose another path")
    result = inventory(root)
    serialized = json.dumps(result, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(serialized, encoding="utf-8")
        print(json.dumps({"output": str(args.output), "files_scanned": result["coverage"]["files_scanned"], "readiness": result["readiness"]}, ensure_ascii=False))
    else:
        sys.stdout.write(serialized)


if __name__ == "__main__":
    main()
