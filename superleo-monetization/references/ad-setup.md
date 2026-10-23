# Set up ads for a game automatically (one approval)

Trigger: "заведи рекламу", "настрой рекламу для X", "подключи все сети", or finishing a partially
set-up game. Networks: AppLovin MAX + AdMob, Unity, Mintegral, Pangle, Meta Audience Network.
This is the default path for ad setup. Use the per-network operations in
[ad-networks.md](ad-networks.md) and [meta-audience.md](meta-audience.md) only for repairs the plan
cannot express or when the user asks for a single network.

## Flow

1. **Identify the game.** `superleo_access` → catalog → `projects` / `project`. Use the exact Superleo
   project ID. v1 supports **Android** projects with `androidPackageName`; for iOS stop with the plan's
   `PLATFORM_UNSUPPORTED` blocker and offer the per-network operations.
2. **Networks and formats.** "Подключи/заведи рекламу" means the team standard: all six networks above;
   the plan always covers all of them. If the user wants to exclude a network, do not apply the plan:
   set up the wanted networks with the per-network operations instead.
   Formats: by default the plan uses the formats that already have MAX units. If the game has none, the
   plan returns `FORMATS_REQUIRED`: ask the user which formats the game shows (offer Rewarded,
   Interstitial, Banner; check the game code if available) and pass them as a comma string,
   `query: {formats: "REWARD,INTER"}`, and the same list as an array in `ads_setup_apply` `body.formats`.
   Never pick formats silently.
3. **Plan (read-only).** `ads_setup_plan` with `params: {id}` and optional `query: {formats}`. It reads
   every network and returns `steps`, `blockers`, `ready` and `planHash`. Reads are cached for 5 minutes.
4. **Show the plan in plain language** (per network: what exists, what will be created/linked, what
   waits for approval) and get **one** explicit approval. Mention anything that creates new resources
   (AdMob Partner bidding units, MAX units for new formats, Meta property, Play listing website).
   Also show `followUps[].summary`: when the plan creates an app/property (AdMob app, Mintegral app,
   Meta property), its later steps (placements/units, saved IDs, MAX hookups for the same formats) get
   concrete IDs only after creation. Approving the plan approves those follow-ups too.
5. **Apply.** `ads_setup_apply`, `params: {id}`, `body: {planHash, formats}` (same formats as the plan),
   `confirm: true`. Call it **once** and wait (up to ~9 min; the client allows 12). Never call it again
   automatically.
6. **Report** from the result: `executed[]` (id, success, code), `halted[]`, the fresh `plan` it returns.
   Then run `ads_approval_status` and report approvals.
7. **Later:** when a network becomes approved, run `ads_setup_plan` again; its remaining steps hook that
   network into MAX. Offer the user to check back (e.g. Meta review takes up to ~72 h).

## Result statuses

| `status` | Meaning / action |
| --- | --- |
| `COMPLETED` | All steps succeeded. Report and check approvals. |
| `COMPLETED_WITH_FAILURES` | Some steps failed; other networks/formats continued. Explain each failed step by `code`/`error`, fix the cause, then build a **new** plan. |
| `PLAN_CHANGED` (409) | State changed since the plan (someone else edited, or a cached plan). Nothing was written. Build a new plan and show differences before applying. |
| `BUDGET_EXHAUSTED` | Time budget ended between steps. Nothing is half-done; build a new plan for the rest. |
| `AD_SETUP_RUNNING` (409) | Another apply for this game is running. Wait for it, then re-plan. |

A failed or ineffective step halts only its network (app-level steps) or its network+format
(placement/unit/MAX steps). `STATE_UNCHANGED_AFTER_STEP` means the provider accepted the call but the
re-read did not show the change (async review, eventual consistency): read again later, do not retry.

## What the plan does per network

- **AdMob:** finds the app linked to the package (or creates one with the Play listing), links the store
  if needed, inspects existing units' saved serving type in the browser (`ADMOB_UNIT_STANDARD` = a
  standard unit, which can never be used for MAX bidding and cannot be converted), creates a Partner
  bidding unit per format (reusing an existing PB unit, never duplicating). Hooks into MAX only after the
  app is `APPROVED`.
- **Unity:** existing Monetize project with `adsProvider=max` (switches mediation if needed), bidding
  placement per format. A missing Unity project is an **input** blocker (COPPA/store data needed).
- **Mintegral:** finds/creates the app for the package, HB placement+unit per format, saves the app ID.
- **Pangle:** existing app (missing app = input blocker: category needed), links the Play URL and requests
  Live, creates bidding placements (banner 640×100). Hooks into MAX only when the app is Live (status 2).
- **Meta Audience Network:** `meta_audience_setup` creates/completes the property in the browser worker:
  property → Android app from the Google Play URL → payout (kept only when exactly one account exists) →
  app-ads.txt review → AppLovin MAX mediation → ad space; then binds it to the project and creates
  placements. If the Play listing has no developer website, `play_set_contact_website` sets it and
  `meta_audience_resubmit` resubmits review (Meta checks `app-ads.txt` on that site). Hooks into MAX only
  after Meta approves the app.
- **MAX:** one hookup per network+format via `max_create`, which reuses the unit and merges networks
  (other networks and settings are preserved). Each hookup is re-verified by the backend (AdMob unit
  type, Pangle slot, Meta readiness).

## Approval status

`ads_approval_status`, `params: {id}` (read-only, also in the panel: Провижены и интеграции → Одобрение
рекламных сетей). Per network: `state` = `approved | pending | action_required | not_required |
not_configured | unknown`, `detail`, and `blocksMax` (MAX hookup waits for this approval). Also returns
`connectedToMax`, remaining `pendingSteps`, and `consent` (AdMob GDPR message, see
[consent.md](consent.md)). Typical waits: AdMob `IN_REVIEW` (days), Meta review
(≤ ~72 h), Pangle verification (status 1).

## Blockers needing a person

Report these with the exact action; do not work around them:

- `META_SETUP_PAYOUT_CHOICE_REQUIRED` — several Meta payout accounts: choose one in Monetization Manager.
- `META_SETUP_STORE_LISTING_NOT_FOUND` — the game is not public on Google Play under this package; publish it
  first with [play-publishing.md](play-publishing.md).
- `META_RATE_LIMITED` — Meta temporarily blocked the business for acting too fast. Stop all Meta work for
  several hours; never retry automatically.
- `META_SETUP_UNEXPECTED_FORM` / `MISSING_CONTROL` / `AMBIGUOUS_CONTROL` — Meta UI changed; nothing was
  submitted at that stage. Needs a worker update.
- `*_UNCERTAIN` codes — a write may have happened. Read state (`ads_setup_plan`) before anything else.
- `UNITY_PROJECT_MISSING` — ask whether the game is child-directed (COPPA), then create the project
  with `unity_create_project` (`adsProvider: "max"`, that `coppa`, Google Play store with the package; see
  [ad-networks.md](ad-networks.md)), save `unityMonetizeProjectId`/`unityMonetizeGameId` with
  `project_network_ids`, and build a new plan. These writes need their own approval.
- `PANGLE_APP_MISSING` — ask for the app category (Pangle `app_category_code`; copy it from a similar
  existing Multicast game in `pangle_apps` only if the user agrees), create it with `pangle_create_app`
  (public Google Play `download_url`), save `panglePublisherAppId`, and build a new plan.
- `*_AMBIGUOUS`, `*_MISMATCH` — identity decisions for the user (which app/unit/property is the game's).
- Worker/tunnel unavailable (`META_WORKER_*`, AdMob provisioning errors): the PC browser services must run;
  ask the operator, do not configure CDP/SSH from the skill.

## Rules

- One approval covers exactly the shown plan (`planHash`). New plan with different steps → show it again.
- Never call per-network create operations "to retry" a failed plan step; re-plan instead.
- Do not delete or disable existing provider resources or MAX networks.
- Provider approval ≠ ads on device. After setup, follow [game-project.md](game-project.md) for SDK/adapters
  and test-mode checks, and [consent.md](consent.md) for EU consent (the AdMob GDPR message must include the
  game's AdMob app).
