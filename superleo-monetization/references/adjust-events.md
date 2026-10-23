# Adjust standard events and analytics

Use the same skill-local connection and approved monetization identity; server credentials remain on SuperLeo.
Read the catalog first: older deployments do not have these operations. Never substitute direct provider calls when a method is missing.

For nested JSON/dynamic keys instead of the 14 legacy flat parameters, read [adjust-game-events.md](adjust-game-events.md). That separate opt-in preset uses the same four MCP operations and preserves provider event identity. Do not flatten away existing game semantics or register every dynamic key as an Adjust definition.

## Entry requests and completion scope

- "Заведи стандартные события для этой игры" means resolve the current game and propose its preset; it does not mean creating events across the account or replacing existing callbacks.
- "Покажи level_started по уровням за период и когорте" means read the report and explain its filters. Do not create missing definitions or deploy infrastructure as a side effect of a report request.
- In this branch the panel permits internal read roles, but creation/MCP monetization still requires root/dev/user. Honor `canCreate` and `superleo_access`; a page being visible does not grant write rights.
- A panel deploy exposes operations and UI, not telemetry readiness. If storage/callbacks are missing, return the deployment prerequisite and offer the next scoped step. Never call an empty report a successful SDK integration.

## Resolve and preview

1. Inspect this game's analytics source and SDK version. Resolve the exact SuperLeo project and its `adjustTrackerToken` using `projects` / `project`; verify platform/package through `adjust_app`. If missing, follow `adjust.md` to bind an existing app or separately create one. Never copy another game's event tokens.
2. Call `adjust_event_standards` (no params). The versioned `multicast-v1` catalog contains 21 definitions derived from the shared Multicast scripts, including proposed readable names for the old token-only Adjust revenue events. The returned `parameters`, `unique`, `source` and `defaultSelected` are authoritative. Do not recreate the list from memory.
3. Call `adjust_events`, params `{"id":"verified-project-id"}`. It returns current definitions with token, unique and archived flags. Failed reads are NOT empty lists. Reuse exact names. If the game already uses different names for the same semantics, reconcile with the user before adding duplicates; do not rename existing provider events.
4. Propose concrete names and which are missing/existing/conflicting. All games may use the preset, but only create for the game(s) explicitly requested. Never mass-provision all projects. `playtime_seconds` is intentionally NOT selected by default: it produces a callback per second; require explicit approval and capacity planning.

Create example (body confirmation is a panel/API safety gate; outer confirmation is the MCP execution gate):

```json
{
  "operation": "adjust_create_standard_events",
  "params": { "id": "verified-project-id" },
  "body": {
    "preset": "multicast-v1",
    "expectedAppToken": "verified_app_token",
    "eventNames": ["first_launch", "level_started", "level_finished", "level_failed"],
    "confirm": true
  },
  "confirm": false
}
```

Outer `confirm:false` only validates a local plan; it is not a provider dry run. Change outer `confirm` to true only for the approved plan. Set `expectedAppToken` from the verified project/event read; if the binding changed, refresh and reconfirm instead of overwriting the guard. Existing events are not updated, unarchived or deleted. Conflicts in `unique`/archived flags require reconciliation. `_first` level events are **not** globally unique: they occur once per level, not once per user for the entire game.

Check `success` AND every `results[].status`: `existing` / `created` include verified tokens; `conflict`, `failed`, `unknown` are unfinished. Read `adjust_events` again after ambiguous outcomes before any retry. Do not automatically retry writes or use app-template copying to repair events. The API acquires a short creation lease but cannot serialize edits made directly in Adjust.

## Wire the game only when requested

Creating definitions does not start tracking (`sdkWired:false`). Inspect the actual game and map semantic names to the returned **per-app** tokens. Do not assume SDK event alias support, even though Adjust App Automation supports aliases: verify the installed SDK before choosing it over tokens.

The inspected ScrewPuzzle shared implementation sends level/start/playtime to AppMetrica, not Adjust; its ad events contain hardcoded Adjust tokens, and revenue parameters use `addPartnerParameter`. Merely creating events will not fix that game code. Do not duplicate existing ad-impression callbacks or report each ad both as IAP revenue and ad revenue.

For Unity SDK v4 (the inspected source uses `com.adjust.sdk`):

```csharp
var evt = new AdjustEvent(gameConfig.AdjustEvents["level_started"]);
evt.addCallbackParameter("level", level.ToString(System.Globalization.CultureInfo.InvariantCulture));
Adjust.trackEvent(evt);
```

Use the installed SDK's names/signatures (v5 differs), the game's existing config reader and event hooks. Preserve existing AppMetrica/Firebase behavior. For the standard preset, normalize dynamic level/time keys to flat parameters, e.g. `{"3":""}` becomes `level="3"`. For nested events and A/B use the universal contract instead; it does not require rewriting those payload shapes. Never put API/callback secrets in client code. Existing partner parameters are distinct from publisher callback parameters; do not promise they arrive without the matching export placeholders.

## Read analytics, not invented history

```json
{
  "operation": "adjust_events_report",
  "params": { "id": "verified-project-id" },
  "query": {
    "from": "2026-10-10", "to": "2026-10-12",
    "cohortFrom": "2026-10-01", "cohortTo": "2026-10-07",
    "environment": "production",
    "eventToken": "returned_token", "parameter": "level"
  }
}
```

- Event dates and install dates are UTC, inclusive calendar dates. Event window max 90 days; paired install-cohort bounds max 366 days. Omit cohort bounds for all installation dates, including unknown installs.
- Omit eventToken to see all events. Parameter drilldown requires one eventToken. `parameterValue` further filters one value and requires `parameter`. Use keys returned by the catalog/report; no arbitrary SQL or JSON paths.
- `total`: all received events after filters. `users`: distinct pseudonymous Adjust devices that sent those events, **not all installed or active players**, not cross-device people. Overall users are computed distinctly, not summed across events/days.
- `average = identifiedEvents / users`; null when no known user IDs. `missingUsers` records cannot be attributed to a user; `missingCohort` records are excluded only by an active cohort filter. These are callback metrics, not a claim of parity with Datascape's modeled totals.
- Production and sandbox are isolated. Data comes from stored callbacks, not report API history. `configured:false` means receiver/storage setup is still required; an empty configured report may mean no arrivals or filters outside stored history. `firstReceivedAt`/`lastReceivedAt` describe retained arrivals, not verified uninterrupted coverage. Retention 90 days; `truncated:true` limits tables to top 100 groups, not totals.
- Panel page: `/adjust-events`. It can display data without downloading files. A failed refresh must not be described as zero events.

## Receiver prerequisite and verification

An operator must deploy the separate telemetry receiver/database per `scripts/adjust-events/README.md` in SuperLeo, configure an authenticated **POST** single-activity event callback in Adjust, and protect ingestion with Adjust IP allowlisting. Do not expose or fetch its secrets through MCP. Do not replace existing callbacks or configure a global callback implicitly. Raw parameters not previously exported cannot be recovered retrospectively with the ordinary report API.

After SDK wiring, run a sandbox test on a device, confirm one known event/level arrives and the repeated callback nonce is deduplicated, then inspect the sandbox report. Obtain separate approval before enabling production collection. Report four states separately: definitions, SDK wiring/build, receiver delivery, real observed data. Neither a successful create response nor `/health` proves the whole integration works.
