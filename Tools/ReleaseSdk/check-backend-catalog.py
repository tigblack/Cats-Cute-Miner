"""Verify only this game's authenticated catalog; never submit a payment."""
import datetime
import json
import os
from pathlib import Path
import urllib.error
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[2]
BASE = "https://backend.api-multicastgames.org"
GAME = "gm_cats_cute_miner"
SESSION = ROOT / "release/google-play/backend-qa-session.local.json"
EVIDENCE = ROOT / "release/google-play/evidence/backend-runtime-catalog.json"


def request(path, body=None, token=None):
    headers = {"Accept": "application/json", "Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(BASE + path, data=data, headers=headers)
    with urllib.request.urlopen(req, timeout=25) as response:
        return json.load(response)


def save_private(data):
    SESSION.parent.mkdir(parents=True, exist_ok=True)
    fd = os.open(SESSION, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, "w") as handle:
        json.dump(data, handle)
    SESSION.chmod(0o600)


def main():
    state = json.loads(SESSION.read_text()) if SESSION.exists() else {"deviceId": str(uuid.uuid4()), "gameId": GAME}
    if state.get("gameId") != GAME:
        raise ValueError("Preserving a QA session for a different game; do not reuse.")
    if not state.get("credentials"):
        save_private(state)
        result = request("/api/auth/login/guest", {"deviceId": state["deviceId"], "gameId": GAME, "createProfile": True})
        credentials = result.get("credentials")
        if not credentials or not credentials.get("authToken"):
            raise ValueError("Guest authentication shape not recognized; no credentials printed.")
        state["credentials"] = credentials
        save_private(state)
    token = state["credentials"]["authToken"]
    response = request("/api/products/get", token=token)
    products = response.get("products")
    if not isinstance(products, list):
        raise ValueError("Runtime catalog shape not recognized; no raw response printed.")
    expected = {"gems_handful": 160, "gems_sack": 500, "gems_bag": 1200, "gems_crate": 2500, "gems_cart": 6500, "gems_pile": 14000}
    checks = []
    for sku, amount in expected.items():
        matches = [p for p in products if p.get("productSku") == sku]
        p = matches[0] if len(matches) == 1 else {}
        ok = p.get("googlePlaySku") == sku and p.get("productType") in ("Consumable", 0)
        rewards = p.get("rewards")
        ok = ok and isinstance(rewards, list) and len(rewards) == 1
        if ok:
            reward = rewards[0]
            ok = reward.get("sku") == "gems" and reward.get("quantity") == amount
            ok = ok and reward.get("nestedItems") in (None, [])
        checks.append({"sku": sku, "quantity": amount, "type": p.get("productType"), "status": "verified" if ok else "failed"})
    receipt = {"game_id": GAME, "checked_at": datetime.datetime.now(datetime.timezone.utc).isoformat(), "status": "verified" if all(x["status"] == "verified" for x in checks) else "failed", "authenticated_guest": True, "checks": checks, "live_payment_submitted": False, "purchase_delivery_verified": False}
    EVIDENCE.write_text(json.dumps(receipt, indent=2) + "\n")
    print(json.dumps(receipt))
    if receipt["status"] != "verified":
        raise SystemExit(1)


if __name__ == "__main__":
    try:
        main()
    except urllib.error.HTTPError as error:
        print(json.dumps({"status": "failed", "http_status": error.code, "raw_response_hidden": True}))
        raise SystemExit(1)
    except Exception as error:
        print(json.dumps({"status": "failed", "error_type": type(error).__name__, "detail": str(error)}))
        raise SystemExit(1)
