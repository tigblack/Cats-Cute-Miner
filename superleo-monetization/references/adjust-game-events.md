# Universal game events and observed A/B analytics

Read `adjust-events.md` first for identity, permissions, confirmation, callbacks and non-retry rules. This is an optional channel, not a replacement for monetization/revenue tracking or an instruction to modify every game.

## Resolve and create the channel

1. Inspect the requested game's actual analytics interfaces, registrations, concrete-class consumers, SDK version and payload shapes. Do not assume changing one DI registration migrates all consumers. Preserve existing providers until the user explicitly approves replacement.
2. Resolve the exact SuperLeo project and linked Adjust app. Read `adjust_event_standards` and `adjust_events`. The catalog must advertise `universalPreset: "game-events-v1"` and `universalEvents`; if absent, the backend needs an update. Do not silently use the standard preset or direct provider credentials.
3. Propose one non-unique Adjust event named `game_event`. Reuse a verified non-archived, non-unique exact match; incompatible or ambiguous matches require reconciliation. Only create for the approved game. Neither a token nor a successful provider write proves SDK/callback readiness.

```json
{
  "operation": "adjust_create_standard_events",
  "params": { "id": "verified-project-id" },
  "body": {
    "preset": "game-events-v1",
    "expectedAppToken": "verified_app_token",
    "eventNames": ["game_event"],
    "confirm": true
  },
  "confirm": false
}
```

Outer `confirm:false` is a local plan, not provider validation. After approval use outer `confirm:true`, check every result, and read definitions back. Save the returned per-app event token in the game's existing config. Never copy a rewarded token or another game's token.

## Version 1 SDK contract

Three custom **callback parameters**, each a string:

| Key | Value |
| --- | --- |
| `game_schema_version` | `"1"` |
| `game_event_name` | Stable logical name, e.g. `shop_buy` or `ab_test_remote` |
| `payload` | JSON-serialized object, e.g. `{"offer_small":{"amount":12.5}}` |

The provider's `event_name` remains `game_event`. Never send a custom `event_name`: it conflicts with Adjust's callback field. Logical names match `[A-Za-z][A-Za-z0-9_.-]{0,95}`. All three custom fields are required together; partial/unsupported/conflicting envelopes are rejected, not saved as an ordinary event.

Unity SDK v5 example (verify the installed SDK signatures and use the existing JSON serializer):

```csharp
using AdjustSdk;

var evt = new AdjustEvent(gameConfig.AdjustGameEventToken);
evt.AddCallbackParameter("game_schema_version", "1");
evt.AddCallbackParameter("game_event_name", "shop_buy");
evt.AddCallbackParameter("payload", "{\"offer_small\":{\"amount\":12.5}}");
Adjust.TrackEvent(evt);
```

Serialize real objects with the existing JSON library, not string concatenation. Unity `JsonUtility` does not automatically solve arbitrary dictionary serialization: inspect the actual game's dependencies. Do not manually URL-encode or double-serialize the `payload` string. Never place receiver credentials in the SDK; the server callback authenticates independently. Do not call `SetRevenue` on this generic channel or duplicate the game's existing IAP/ad-revenue reports.

Limits enforced by SuperLeo (not a promise of Adjust/proxy transport limits): payload **8192 UTF-8 bytes**, object root, max depth 8, 256 visited nodes, 128 scalar leaves; keys max 128 characters, string values max 1024, flattened index max 32 KiB. Nested objects and arrays are supported. Empty objects/arrays persist but have no scalar drilldown leaf. Reject oversized data at the game adapter too; do not silently truncate or split one event into inflated counts. A failed event must produce safe local diagnostics without printing its payload.

Invalid Unicode, non-finite numbers and numeric exponent expansion that would exceed the JSONB storage budget are rejected too. Counts or external identifiers requiring integer precision beyond JavaScript's safe range must not be sent as numeric analytics values; identifiers/PII are not supported payload content.

Use a reviewed per-game allowlist of gameplay fields, even though arbitrary gameplay shapes are supported. Do not forward arbitrary AppMetrica objects blindly: exclude emails, names, account/device IDs, free text, addresses, credentials and other personal data. The receiver rejects known sensitive/prototype key names recursively and email-like values, but cannot prove absence of personal information in arbitrary strings or dynamic keys. Hashing `adid` does not anonymize arbitrary payload content. Respect the game's consent/privacy handling.

## A/B assignment contract

Send a logical `ab_test_remote` **when the effective assignment becomes known, before attributed gameplay**:

```json
{"offer_test":"AB_B","difficulty_test":"control"}
```

The payload must have 1–32 top-level experiment-to-variant string pairs (nonempty, trimmed, max 128 characters each). Dynamic experiment names are supported; nested assignment objects and numeric variants are rejected. Keep names stable; a materially new experiment needs a new name. This observes an assignment; it does not change remote config or allocate players to variants.

The report uses app/environment-scoped, identified callbacks in the retained 90-day history, up to the end of the report (and not future timestamps). An event belongs to a variant only at or after the user's first observed assignment. Repeated identical assignments are safe. Users observed in more than one variant of the selected experiment by that cutoff are excluded entirely; warnings report `conflictingUsers` and `excludedEvents`. Unknown identity, missing assignment and pre-assignment events are excluded from A/B comparisons too. Delayed callbacks can revise a report. No history before retention/collection can be inferred.

Legacy variant `users` counts senders of the **selected event after filters**, not all assigned users. `average` is identified events / these senders. The opt-in `analysis` fields below provide assigned-user denominators and comparisons; never present either as a guaranteed randomized-experiment result.

## Read the dashboard / MCP

Open `/adjust-events`, select the project and dates/environment. A row identifies both the provider token and logical game name. Select the logical event, choose a payload path and optionally click a typed value. Select an experiment and variant to compare observed cohorts.

```json
{
  "operation": "adjust_events_report",
  "params": { "id": "verified-project-id" },
  "query": {
    "from": "2026-10-10", "to": "2026-10-12",
    "environment": "sandbox",
    "eventToken": "returned_game_event_token",
    "gameEventName": "shop_buy",
    "payloadPath": "/offer_small/amount",
    "payloadValue": "12.5",
    "experiment": "offer_test", "variant": "AB_B"
  }
}
```

- Omit optional value/experiment/variant filters for broader data. Date and install-cohort rules are unchanged. `gameEventName` requires `eventToken`; `payloadPath` requires both; it cannot be combined with legacy `parameter`. `variant` requires `experiment`.
- Use returned `payloadPaths` (JSON Pointer). `/offer_small/amount` is nested; literal key `a/b` becomes `/a~1b`, literal `x~y` becomes `/x~0y`; arrays use `/items/0/amount`. These are not executable SQL/JSONPath expressions.
- `payloadValue` is a **JSON-encoded scalar string**: `12.5` versus `"12.5"`, `true`, `null`. In an outer JSON request, a string value uses `"payloadValue":"\"12.5\""`. A missing path is distinct from JSON null and is shown separately; missing-value filtering is not exposed.
- `payloadBreakdown` carries typed `value`, `missing` and normal event metrics; `experiment.variants` shows all eligible variants before the optional variant filter. Other date/event/payload/cohort filters still apply. `conflictingUsers`/`excludedEvents` concern the selected candidate events, before variant filtering. `experiments` discovery includes observed assignments up to the report end, not necessarily in the selected event period.
- Tables/discovery lists cap at 100; heed `truncated`. Totals remain exact for the filters. No raw users are exposed.

## Extended analytics through the same report operation

Use the existing `adjust_events_report` with `query.analytics="true"`. This reads telemetry only; do not create events, change callbacks, deploy a database or alter a game's assignments as a side effect. If the deployed catalog/backend does not accept the fields, report that an update is required rather than silently substituting another API.

```json
{
  "operation": "adjust_events_report",
  "params": { "id": "verified-project-id" },
  "query": {
    "analytics": "true", "history": "raw",
    "from": "2026-10-10", "to": "2026-10-12", "environment": "sandbox",
    "eventToken": "returned_game_event_token", "gameEventName": "shop_buy",
    "experiment": "offer_test", "control": "AB_A",
    "expectedShares": "{\"AB_A\":0.5,\"AB_B\":0.5}",
    "numericPath": "/offer_small/amount",
    "cohortFrom": "2026-10-01", "cohortTo": "2026-10-05",
    "returnEvent": "level_started",
    "funnel": "[\"shop_open\",\"shop_buy\"]", "funnelWindowHours": "24"
  }
}
```

Choose actual logical names and paths from this game's source/report; example names are not a universal preset. Omit experiment/control/shares for a non-A/B report. Omit `returnEvent`/`activityEvent` for any callback, or set each to a stable logical name. `eventToken`/`gameEventName` select the conversion/numeric target, not the return/activity event. Install dates define the cohort denominator; `to` must extend far enough to observe returns. Dates are inclusive UTC, max 90 report days and 366 cohort days; no future dates.

- `analysis.ab.variants.assignedUsers` includes observed assignments through the cutoff, including zero buyers; `convertedUsers` requires the target in the report window at/after assignment. Control defaults to alphabetically first. `expectedShares` is a JSON object of 2–20 positive fractions summing to one; all observed labels must be covered, expected missing labels become zero groups. Conflicted users are excluded.
- Conversion p-value is two-sided pooled z; `ci95` is the **absolute difference in proportions**, not uplift CI. Uplift is undefined for zero control conversion. `sufficient=false` must never be presented as a significant win. Minimums are 30 users, 5 successes and 5 failures in each arm. SRM uses χ²; `warning` needs expected counts ≥5 and p<.001. Comparisons are unadjusted observational pairwise tests.
- `numericPath` aggregates only numeric JSON leaves (no string coercion) into count/sum/avg/p50/p90. Missing, non-numeric and magnitude>1e100 are diagnostic, not silently valid zeros. Welch compares **per-assigned-user sums including nonbuyer zeros** with sample variance, not independent event values. Invalid/missing numeric values suppress mean significance.
- Only add `revenueCurrency` (e.g. `USD`) after verifying the field means money in that single currency. There is no automatic currency detection/conversion. ARPU = sum / assigned users; ARPPU = sum / positive-total users. A crystal count or gameplay amount is not revenue by default.
- Retention D1/3/7/14/D30 uses exact UTC install-day offsets; unknown/conflicting installation dates are reported separately. Immature/uncovered cells are null, not zero. DAU/WAU/MAU are distinct callback users over 1/7/30 days; stickiness = DAU/MAU. Do not sum daily uniques. Check `complete`.
- Funnel is 2–5 logical events in one channel, strictly increasing timestamps, 1–720 hours from a start (default24), within report dates. A later start can complete a path. More than10000 start events is an explicit error: narrow the period/cohort, do not repeatedly request the same overload.
- `history="daily"` reads operator-built 400-day aggregates; it is not a live fallback. It cannot use parameter/payload value filters or funnels. Numeric paths must have been configured and rolled up while raw still existed. Check `coverage.missingDays`, `pendingDays`, `numericPathTracked`; missing/stale coverage disables inference. Daily retains up to400 days of **observed** assignment history versus raw90, so denominators need not match. No raw user identifiers are returned.
- Panel CSV exports the current bounded snapshot, filter set, warnings and coverage, not all raw events. When using MCP, return the same read response or locally format that aggregate response; do not retrieve database credentials to export.

## Deployment and acceptance

An operator must apply updated `scripts/adjust-events/schema.sql` to the **separate telemetry database before** restarting the new panel/receiver. The additive migration keeps old flat events readable; new collection requires the new receiver. Follow its README for bounded locks, proxy limits and callback setup. The game agent must report an infrastructure blocker rather than claiming an empty graph proves success.

The existing authenticated POST callback must deliver these custom parameters either as URL-appended SDK callback parameters or inside the JSON-string `publisher_parameters`. Inspect a sandbox delivery privately: supported placeholder spelling varies by exporter. Identity, auth and provider event name stay in the POST body; do not repurpose them. Conflicting duplicates are rejected. Adjust does not store callback parameters for later ordinary report retrieval; there is no retroactive recovery here.

Acceptance: run the game's build; send `shop_buy` with nested amount plus `ab_test_remote` before it on a sandbox device; confirm logical rows, typed drilldown and correct variant; test an earlier event and conflicting variant are excluded; repeat a callback nonce to verify deduplication. Test the largest approved payload across the real Adjust/proxy path. Report separately: provider token, local adapter/build, actual delivery, displayed data. Production enablement is a separate approval. Do not disable AppMetrica before parity is verified.

Sources: [Adjust Unity callback parameters](https://dev.adjust.com/en/sdk/unity/features/events/#add-callback-parameters), [POST callbacks](https://help.adjust.com/en/article/server-callbacks). Local limits and A/B attribution semantics above are SuperLeo behavior, not Adjust reporting guarantees.

Implementation verification: synthetic mathematical, SQL/WASM and mocked-UI tests cover the analytics paths; native PostgreSQL load/concurrency, deployment, callbacks and a live Devour sandbox device are separate unchecked steps. The SDK v1 envelope and prohibited-field/PII rules are unchanged. An empty report or a created token does not prove collection works.
