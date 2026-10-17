# SuperLeo: advertising monetization contracts

Contracts adapted from the supplied studio superleo-monetization skill. Use [superleo.md](superleo.md) for the current user's own connection and inherited authorization. Configure AdMob, Unity Ads, Mintegral and Pangle by default; an explicit project network set takes priority. Per-network apps/placements, MAX configuration and official adapters are all required parts of setup. Do not report mediation complete from MAX units alone; provider failures remain individually pending while independent networks continue.

These operation names map to existing Superleo panel handlers. Check the runtime catalog before execution; do not infer support for additional networks from a provider's generic API. Use `superleo_monetization` with `operation`, optional `params`, `query`, `body`, and `confirm` (false by default).

## Read first

- `projects`; `project`, params `{id}`: discover existing project IDs and saved network mappings.
- `connections`: existing admin-protected connection diagnostics. A 403 is not proof that a network is disconnected; do not escalate identity. Use allowed network reads or ask an administrator to check connectivity.
- `max_units`, query `{package_names: "com.example.game", platform: "android"}`; `max_unit`, params `{id}`.
- `admob_apps`, `admob_units`: API reads may return success with an empty list **and a message describing a provider error**; treat that as unknown, not absence.
- `unity_projects`; `unity_project`, params `{projectId}`; `unity_units`, params `{projectId, store: "google"|"apple"}`. Unity project ID and store Game ID are different identifiers.
- `mintegral_apps`, query `{package: "com.example.game", os: "ANDROID"}`; `mintegral_placements`, query `{app_id}`.
- `pangle_apps`; `pangle_placements`, query `{app_id}`.

Для Mintegral MAX может требовать `ad_network_app_key` дополнительно к app/unit/placement IDs. Список `mintegral_apps` не обязательно возвращает ключ: ID и publisher API credentials не заменяют App Key. Если фактический `max_update` отклонён с `Missing ad_network_app_key`, получи ключ собственной игры штатным доступным маршрутом панели/официального кабинета; пока такого маршрута нет, отметь binding незавершённым и продолжай другие сети. Не включай network по неполной конфигурации. Pangle BANNER также зависит от допустимых upstream размеров: ошибка hardcoded helper не означает отсутствие созданных rewarded/interstitial и не требует их пересоздавать.

## Create provider resources

| Operation | Body fields |
| --- | --- |
| `admob_create_app` | `platform: android|ios`, `displayName`, optional `appStoreId` |
| `admob_create_unit` | `appId`, `displayName`, `adFormat: REWARD|INTER|BANNER` |
| `unity_create_project` | `name`, `adsProvider`, `coppa`, `stores` (native Unity store configuration; use the existing project's shape or ask for missing data) |
| `unity_create_unit` | Params `{projectId, store: google|apple}`; body `adFormat: rewarded|interstitial|banner`, optional `customName`, `customPlacementId`, `placementType: bidding|waterfall`, format-specific settings |
| `unity_set_mediation` | Params `{projectId}`; body `{adsProvider: "max", expectedAdsProvider: "<value just read>"}`; preserves existing COPPA and store settings |
| `mintegral_create_app` | `app_name`, `os: ANDROID|IOS`, `package`, optional `is_live_in_store: 0|1`, `store_url`, `coppa`, `mediation_platform`, `store` |
| `mintegral_create_placement` | `app_id`, `ad_format: REWARD|INTER|BANNER`, `platform: android|ios`, optional `name` |
| `pangle_create_app` | `app_name`, numeric `app_category_code`, optional `download_url`, `coppa: -1|0|1` |
| `pangle_create_placement` | numeric `app_id`, `ad_format: REWARD|INTER|BANNER`, `name`, optional `orientation: 1|2` |

Do not guess the meaning of a provider's numeric compliance/category enums. Ask the operator or use its verified existing config. Unity's backend defaults to `adsProvider: unity`, `coppa: non_compliant`; do not silently assume those are right for MAX mediation or child-directed treatment.

For Unity in MAX bidding, create the project with `adsProvider: max`. For an existing project, inspect its mediator first; when the user has approved MAX setup, use `unity_set_mediation` if it is present in the deployed catalog. Its expected-value guard prevents overwriting a mediator changed since discovery, and the backend echoes the existing COPPA value required by Unity without changing the declaration. Read back the project after a failure before retrying. Create the intended placement with `placementType: bidding` and verify the returned/read-back type before configuring `UNITY_BIDDING`. Default placements may be waterfall; their existence is not evidence of bidding readiness. Reuse exact existing bidding placements; do not recreate or remove unrelated defaults.

For a new unpublished game, Unity may reject `stores.google.storeId` because it
cannot find the package in Google Play. Inspect the actual failure and reread the
project list first. The verified prelaunch path for the existing panel contract is
`{name, adsProvider:"max", coppa:<confirmed audience enum>}` **without `stores`**;
Unity generates store Game IDs and default bidding placements. An empty google
object is not equivalent: this provider rejects an undefined storeId. Save the
own creation receipt and attach the Game ID to the correct panel project; record
store/package binding as pending until the real Play listing is available. Never
substitute another game's package. Use the current panel contract if it changes.
The optional store fields are described in [Unity Monetize API](https://docs.unity.com/en-us/oas-monetize/1.0.0).

### AdMob: API first

Use `admob_create_app` and `admob_create_unit` through Superleo MCP when present in the deployed catalog. Verify provider provisioning permission for the actual account; an earlier operator report for another publisher is not a live write check.

Read `admob_apps` and `admob_units` first and reuse verified existing resources. Do not create a duplicate solely to test permissions. After an authorized API creation, read the returned IDs back and persist `googleAdmobAppId` with `project_network_ids`. The API path does not need the browser worker or tunnel, but still needs valid server-side OAuth configuration. App approval such as `ACTION_REQUIRED` is separate from API provisioning access.

If the API explicitly rejects provisioning permission, report the error and offer browser fallback; do not silently switch transports. When the same setup is already authorized and readback verifies that the API request did not create a resource, a supported configured panel browser fallback may use `admob_browser_health`, then:

- `admob_browser_app`: `{projectId, name, platform: android|ios, storeId?, linkToStore?}`. Uses the configured signed-in browser and persists `googleAdmobAppId`. Existing app conflicts must be reconciled, not forced past.
- `admob_browser_unit`: `{projectId, appId, name, adFormat: REWARD|INTER|BANNER}`. Requires that app ID on the project. The backend can reuse an existing app/format unit.

Do not fall back on timeout alone or configure CDP/SSH from the skill. A missing service/login is an operator dependency.

## Configure MAX last

`max_create` body:

```json
{
  "name": "Game Android Rewarded",
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

The create handler reuses an existing MAX unit for the same package/platform/format and may replace its network settings. For an existing unit, read `max_unit` first and preserve all unrelated mediation, caps and floors. Use `max_update` with params `{id}` and the full required identity (`id`, `platform`, `package_name`, `ad_format`) plus merged changed configuration. Do not disable/remove existing networks as an incidental side effect of adding one. If existing config cannot be safely preserved, stop.

The verified existing-unit UI update uses native `ad_network_settings`, e.g.
`[{UNITY_BIDDING:{disabled:false,ad_network_app_id:"<own Android Game ID>",ad_network_ad_units:[{ad_network_ad_unit_id:"<actual bidding placement ID>"}]}}]`.
Merge this into the current array, preserving other keys even when one array item
contains multiple networks. Flat `unity_enabled`/IDs above describe the create
handler; do not assume update uses that DTO. Read back the network/IDs/enabled state
and unchanged caps/floors after writing.

## Official adapters in the game

Use the installed MAX Integration Manager's live catalog for compatible packages,
then install official adapters through its public downloader/import workflow.
For Unity, `LoadPluginData`/`DownloadPlugin(network, false)` are driven by editor
coroutines; `AssetDatabase.ImportPackage` imports the actual packages. Check their
dependency XML, resolved Gradle/native versions and an Android build. A downloaded
package or source XML alone is not a resolved native integration.

An unused legacy `com.unity.ads` package may include its own Android AAR and conflict
with MAX's Unity Ads dependency. Check gameplay references first; remove an unused
package through `UnityEditor.PackageManager.Client`, or reconcile a used integration
without duplicate native SDKs. Preserve the game's MAX and analytics layers.

Google's adapter requires **this game's own AdMob App ID** in
`AppLovinSettings.Instance.AdMobAndroidAppId`; preserve/set it through the official
settings API and verify the merged manifest. If provisioning is unavailable,
stage the official Google package and leave that network pending. Do not activate
a GMA native build with a missing ID or substitute a sample/reference game's ID.
Continue the other adapters. [MAX official setup](https://support.applovin.com/en/max/unity/preparing-mediated-networks).

Persist returned app mappings with `project_network_ids`, params `{id: "Superleo project ID"}`, body containing only changed fields:
`googleAdmobAppId`, `unityMonetizeProjectId`, `unityMonetizeGameId`, `mintegralPublisherAppId`, `panglePublisherAppId`.
No project identity, permissions or unrelated settings can be changed through this operation. Read the project and MAX unit again to verify.

### Проверено 2026-10-13/14 (Rabbit Gunner, личный ключ `slmcp_…` в `release/google-play/superleo.local.env`)
- `admob_create_app` body: `{platform:"ANDROID", displayName}` (поля `projectId/name/storeId` не принимаются: «displayName required»). Результат — `ca-app-pub-…~…`, статус `ACTION_REQUIRED` до привязки к опубликованному магазину; затем `project_network_ids {googleAdmobAppId}`.
- `admob_create_unit` body: `{appId, displayName, adFormat:"REWARD|INTER|BANNER"}` (не `REWARDED`).
- Записи через `superleo.mjs` идут с `--private-output`; файлы `*.local.json` на хаб не синхронизируются — результат проверяй повторным чтением (`admob_apps`/`admob_units`), не файлом.
- Команда `node` выполняется на ПК (`on-remote`): пути в `--private-output` относительны корня проекта на ПК.
