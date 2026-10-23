# Advertising monetization contracts

For setting up a game's ads end to end use [ad-setup.md](ad-setup.md) (`ads_setup_plan` →
approval → `ads_setup_apply`); the operations below are its building blocks and repair tools.

These operation names map to existing Superleo panel handlers. Check the runtime catalog before execution; do not infer support for additional networks from a provider's generic API. Use `superleo_monetization` with `operation`, optional `params`, `query`, `body`, and `confirm` (false by default).

## Read first

- `projects`; `project`, params `{id}`: discover existing project IDs and saved network mappings.
- `connections`: existing admin-protected connection diagnostics. A 403 is not proof that a network is disconnected; do not escalate identity. Use allowed network reads or ask an administrator to check connectivity.
- `max_units`, query `{package_names: "com.example.game", platform: "android"}`; `max_unit`, params `{id}`.
- `admob_apps`, `admob_units`: API reads may return success with an empty list **and a message describing a provider error**; treat that as unknown, not absence.
- `unity_projects`; `unity_project`, params `{projectId}`; `unity_units`, params `{projectId, store: "google"|"apple"}`. Unity project ID and store Game ID are different identifiers.
- `unity_placements`, query `{projectId, store: "google"|"apple", adUnitId}`: read actual placements when the unit response omits their bidding type.
- `mintegral_apps`, query `{package: "com.example.game", os: "ANDROID"}`; `mintegral_placements`, query `{app_id}`.
- `mintegral_units`, query `{placement_id}`: read the exact units under a placement; a placement ID is not its unit ID. Missing type fields remain unknown.
- `pangle_apps`; `pangle_placements`, query `{app_id}`.
- For Meta Audience Network use [meta-audience.md](meta-audience.md): `meta_audience_status`, `meta_audience_properties`, `meta_audience_inspect` are project-scoped. Do not use `facebook_apps` as placement discovery.

## Create provider resources

| Operation | Body fields |
| --- | --- |
| `admob_create_app` | `platform: android|ios`, `displayName`, `appStoreId` (Android package / numeric iOS store ID); omit only with explicitly approved `allowUnpublished: true` |
| `admob_create_unit` | `appId`, `displayName`, `adFormat: REWARD|INTER|BANNER`, **`servingType: partner_bidding` for MAX** (browser-backed); `standard` is explicit API-only creation, not suitable for Google bidding |
| `admob_verify_bidding` | `appId`, `adUnitId`; reads saved settings in the browser, checks type/app/format, records verification; does not edit the provider |
| `admob_link_store` | Existing `appId`, `platform`, `appStoreId`; browser-backed linking without creating a replacement app |
| `admob_gdpr_status` | `query: {appId}`; read-only: the app's place in the published GDPR message, privacy URL, missing ad partners ([consent.md](consent.md)) |
| `admob_gdpr_add_app` | `appId`; adds the app to the single published GDPR message (sets the studio privacy URL if the app has none); normally run as plan step `admob:gdpr` |
| `unity_create_project` | `name`, `adsProvider`, `coppa`, `stores` (native Unity store configuration; use the existing project's shape or ask for missing data) |
| `unity_create_unit` | Params `{projectId, store: google|apple}`; body `adFormat: rewarded|interstitial|banner`, optional `customName`, `customPlacementId`, `placementType: bidding|waterfall`, format-specific settings |
| `unity_set_mediation` | Params `{projectId}`; body `{adsProvider: "max", expectedAdsProvider: "<value just read>"}`; preserves existing COPPA and store settings |
| `mintegral_create_app` | `app_name`, `os: ANDROID|IOS`, `package`, optional `is_live_in_store: 0|1`, `store_url`, `coppa`, `mediation_platform`, `store` |
| `mintegral_create_placement` | `app_id`, `ad_format: REWARD|INTER|BANNER`, `platform: android|ios`, optional `name` |
| `pangle_create_app` | `app_name`, numeric `app_category_code`, public `download_url`, `coppa: -1|0|1`; omit URL only with approved `allowTestMode: true` (not production-ready) |
| `pangle_link_store` | Existing numeric `app_id`, public `download_url`, `expectedStatus` and `expectedDownloadUrl` (empty string when absent) copied from a fresh `pangle_apps` read; requests Live with the existing category preserved |
| `pangle_create_placement` | numeric `app_id`, `ad_format: REWARD|INTER|BANNER`, `name`, optional `orientation: 1|2`; bidding banners are created at 640×100 (Pangle rejects 320×50) |

Do not guess the meaning of a provider's numeric compliance/category enums. Ask the operator or use its verified existing config. Unity's backend defaults to `adsProvider: unity`, `coppa: non_compliant`; do not silently assume those are right for MAX mediation or child-directed treatment.

For Unity in MAX bidding, create the project with `adsProvider: max`. For an existing project, inspect its mediator first; when the user has approved MAX setup, use `unity_set_mediation` if it is present in the deployed catalog. Its expected-value guard prevents overwriting a mediator changed since discovery, and the backend echoes the existing COPPA value required by Unity without changing the declaration. Read back the project after a failure before retrying. Create the intended placement with `placementType: bidding` and verify the returned/read-back type before configuring `UNITY_BIDDING`. Default placements may be waterfall; their existence is not evidence of bidding readiness. Reuse exact existing bidding placements; do not recreate or remove unrelated defaults.

### AdMob: API apps, browser-verified Partner bidding for MAX

Use API-backed `admob_create_app` for apps. The operator reported Google granted API provisioning permissions for publisher `pub-3038866384444944` on 2026-10-08; that permission does not make ordinary units suitable for MAX bidding. The public [AdUnit API](https://developers.google.com/admob/api/reference/rest/v1beta/accounts.adUnits) does not document a Partner bidding field. `servingType` is a Superleo contract, not a fabricated Google API parameter.

Read `admob_apps` and `admob_units` first. Match app identity, platform and format **and serving type**.
`servingTypeSource: "saved"` marks a verdict saved from an earlier browser inspection (serving type is
fixed at creation); it is good for planning, while MAX attachment always re-inspects the unit. Unknown type or a standard unit is not reusable as Partner bidding. `admob_units` combines API results with browser verification receipts younger than 15 minutes; `verifiedAt` is historical, not current serving approval. Expired receipts are not returned as verified; use `admob_verify_bidding` for existing unknown/expired units. MAX attachment always rechecks saved browser settings. Do not trust an agent-supplied `verified` flag or only the selected checkbox before saving.

For a published game, send `appStoreId` and check returned/read-back `linkedAppInfo.appStoreId` and `appApprovalState`. For an existing unlinked app, use `admob_link_store` after confirming its identity; don't create another app. `APPROVED` is required before enabling Google bidding in MAX. `ACTION_REQUIRED`, `IN_REVIEW` and unknown states are incomplete setup, not successful serving.

Create MAX-bound units with `servingType: partner_bidding`. This needs the configured AdMob browser worker/tunnel/login; app creation via API does not. If the worker is unavailable, stop that step with the dependency. Never use `standard` to make the request succeed. According to [Google](https://support.google.com/admob/answer/12374122), a saved standard unit cannot be converted: propose new Partner bidding units and replacing only their MAX mappings after approval. Preserve existing MAX unit IDs, unrelated networks and old AdMob resources. Do not delete or migrate a whole account automatically.

Persist `googleAdmobAppId` with `project_network_ids` and read it back. Browser/API success proves resource creation only, not approval, production ads, revenue or absence of SDK test mode.

If the API explicitly rejects provisioning permission, report the error and offer browser fallback; do not silently switch transports. Only after operator agreement and verifying that the API request did not create a resource, use `admob_browser_health`, then:

- `admob_browser_app`: `{projectId, name, platform: android|ios, storeId, linkToStore: true}`; without store linking, explicit `allowUnpublished: true` is required. Existing app conflicts must be reconciled, not forced past.
- `admob_browser_unit`: `{projectId, appId, name, adFormat: REWARD|INTER|BANNER, servingType: partner_bidding}`. Requires that app ID on the project and reuses only verified bidding units of that format.

Do not fall back on timeout alone or configure CDP/SSH from the skill. A missing service/login is an operator dependency.

### Pangle: store link and Live are separate checks

For a published game include its verified public Google Play/App Store URL in `download_url`. The backend no longer silently chooses Test when it is missing. An approved unpublished/test workflow may use `allowTestMode: true`, but report the network unfinished for production; do not fabricate a public listing from a package name.

For an existing Test/unlinked app, read `pangle_apps`, verify identity, and propose `pangle_link_store` for that same app ID. This requests Test → Live and needs explicit approval; it preserves category and does not change COPPA or blocking rules. Pass the exact freshly-read status/URL guards. A different existing store page is a conflict, not permission to overwrite it.

Pangle may accept a write for asynchronous verification (`pendingVerification: true`, status 1 / provider code 50007). Retain the app ID/request ID, read `pangle_apps` later, and never repeat creation as a retry. Confirm the correct `download_url`, app status 2 (Live), and placement app/format/`bidding_type: 1`/status 2 before enabling `TIKTOK_BIDDING`. Missing/unknown fields block readiness; green MAX configuration does not establish Pangle approval. See [Pangle Management API](https://www.pangleglobal.com/integration/management-api).

## Configure MAX last

Meta placement discovery/creation and operator prerequisites are in [meta-audience.md](meta-audience.md).
After binding the verified property, add `meta_enabled: true` and `meta_placement_id` to `max_create`
only when authorized and ready. For updates use `FACEBOOK_NETWORK` with Placement ID only, no Meta App ID/key.
The backend verifies the exact project/property/placement before enabling it. This does not automate
payout, terms, property/store setup or review; never omit those blockers from an "all networks" report.

`max_create` body:

```json
{
  "name": "Game Android Rewarded",
  "projectId": "<Superleo project ID>",
  "platform": "android",
  "package_name": "com.example.game",
  "ad_format": "REWARD",
  "admob_enabled": true,
  "admob_app_id": "<returned AdMob app ID>",
  "admob_ad_unit_id": "<returned AdMob unit ID>",
  "unity_enabled": true,
  "unity_game_id": "<returned store Game ID, not Unity project ID>",
  "unity_placement_id": "<returned Unity placement ID>",
  "mintegral_enabled": true,
  "mintegral_app_id": "<returned app ID>",
  "mintegral_unit_id": "<returned unit ID>",
  "mintegral_placement_id": "<returned placement ID>",
  "pangle_enabled": true,
  "pangle_app_id": "<returned app ID>",
  "pangle_placement_id": "<returned slot ID>"
}
```

Replace every illustrative value with verified IDs. Include only authorized, successfully configured networks. MAX uses `ADMOB_BIDDING`, `UNITY_BIDDING`, `MINTEGRAL_BIDDING`, `TIKTOK_BIDDING` (Pangle). Mintegral app credentials are supplied by the backend, not the user.

AdMob and Pangle activation now returns `actionRequired` when store/approval/type checks fail; the request is not a successful MAX attachment. Fix the named prerequisite and re-read state instead of resubmitting identical writes. Leave already-working unrelated networks intact. An incomplete network may be explicitly omitted from an approved partial setup, but must remain listed as blocked in the result.

The create handler reuses an unambiguous existing MAX unit for the same package/platform/format and merges changed networks, preserving unrelated entries. For an existing unit, read `max_unit` first and preserve unrelated mediation, caps and floors. Use `max_update` with params `{id}` and the full required identity (`id`, `platform`, `package_name`, `ad_format`) plus changed configuration. Include `projectId` for iOS: the backend resolves its saved `iosAppId` and matching `iosBundleId` before checking the network store listing. A platform match alone is insufficient. Do not disable/remove existing networks as an incidental side effect of adding one. If existing config cannot be safely preserved, stop.

Persist returned app mappings with `project_network_ids`, params `{id: "Superleo project ID"}`, body containing only changed fields:
`googleAdmobAppId`, `unityMonetizeProjectId`, `unityMonetizeGameId`, `mintegralPublisherAppId`, `panglePublisherAppId`.
No project identity, permissions or unrelated settings can be changed through this operation. Read the project and MAX unit again to verify.
