# Guided setup from a game repository

This workflow is discovery-driven. There is no universal Unity prefab, purchase catalog, or SDK configuration path. Do not create files with guessed schemas and claim the game consumes them.

## 1. Inspect without modifying

Read the repository's instructions, then check `git status --short` if it is a Git checkout. Preserve user changes. Start with `rg --files` or the available file-listing tool. Restrict searches to this game; skip generated builds, caches, installed dependencies and credentials. If multiple games/targets exist, identify the requested one before writes.

Use these as search starting points, not as authoritative configuration by themselves:

| Project clues | What to establish |
| --- | --- |
| Unity `ProjectSettings/ProjectVersion.txt`, `ProjectSettings/ProjectSettings.asset`, `Packages/manifest.json`, `Assets/` | Editor/SDK versions, target-specific identifiers, store/catalog assets, ad/IAP service implementations and their serialized config consumers |
| Android Gradle settings and module build files, manifests | Effective application ID for the intended flavor/build, not just namespace or a generated manifest |
| iOS project/config files, plists | Bundle identifier for the selected target/configuration; this is not the numeric App Store ID |
| Godot `project.godot`, export presets, scripts | Export target identifiers and installed ad/IAP plugins with their real initialization paths |
| Unreal `.uproject`, `Config/`, project source/plugins | Build target, package settings and plugin/config consumers |
| Other layouts | Identify the build entrypoint and monetization abstractions before proposing edits; do not assume Unity |

Follow build-script overrides and flavor-specific settings. Check relevant files, not the entire environment or secret store. In a synced workspace, respect its rules for ignored/binary assets: an absent local file may require a read on the owning machine, not recreation.

Trace both ends of each setting: where the ID/SKU is declared and where runtime code loads/uses it. Find existing wrappers/services before adding new direct SDK calls. For purchases, find reward identifiers and fulfillment/validation paths, not just the shop label. Record engine/version, target, IDs, integration locations and unknowns with file references.

Do not upload source code, signing files or whole config files to Superleo. Submit only the fields needed for the approved operation. MCP tokens, provider management keys, private signing keys and OAuth tokens must not be written into game source or assets. SDK-specific client configuration is distinct from a server management credential; use the project's established handling and do not echo its value in reports.

## 2. Match and ask only what is missing

Use `projects` and `project` from the catalog to match platform + package/store ID. A display-name match is only a candidate. Multiple exact matches require selection. Never treat a failed read as an empty account.

If no project exists, propose `project_create`, body `{name, platform: "android"|"ios", androidPackageName?, iosAppId?}`. Android requires a verified package name. iOS requires an actual numeric App Store ID (optional `id` prefix); a bundle ID cannot fill this field. If the app has no store ID yet, explain this limitation instead of inventing one. New projects do not automatically create a Multicast game or provider apps.

Before creating remote resources, identify local blockers too. If no supported integration exists, ask whether to proceed with provider-only setup or include installing/integrating an SDK. Do not create paid/live product definitions and only afterward reveal that the local integration is unknown.

For Google Play purchases, first follow the billing-enabled AAB/release prerequisite
in [iap.md](iap.md). If missing, offer SDK setup and AAB preparation before product
creation. Treat this as an explicit prerequisite phase before applying remote
results below; upload/release requires separate approval and suitable tooling.

Examples of concise questions:

- Ads: "В проекте уже подключён MAX и есть rewarded. Добавляем interstitial тоже? Нужны все пять сетей?" Only ask what the request/code hasn't answered.
- Purchases: "Нашёл магазин монет. Какие наборы и цены нужны? Предложить варианты?" Proposals must be approved before creation; keep draft suggestions out of provider calls.
- Missing authorization: first check the skill-local config and shared team scope using `connection.md`. A working team key needs no personal key. Only when no suitable connection exists, explain the one-time administrator setup; never request provider passwords.

Show one bounded plan listing game/targets, provider changes, products/prices/rewards, local files to edit and remaining manual dependencies. User approval covers that plan's dependency steps. Stop for a new choice if an unexpected change would broaden it.

## 3. Apply remote results to actual local consumers

Read the ads/IAP reference for payload contracts. Execute remote steps with explicit confirmation and reconcile ambiguous responses before retries. Track returned IDs per provider, platform and format; IDs from a different platform are not interchangeable.

Then make minimal local edits:

### AppLovin client SDK key

The operator can supply `APPLOVIN_SDK_KEY` in `assets/.env.sdk.local`, relative to
the installed skill. Read this file programmatically without emitting its contents
to the model/logs; parse data, never source/evaluate it. Verify it is ignored and
untracked in the destination repository before storing a value. The bundled MCP
client does not load this file: the local integration/build step must explicitly
transfer only `APPLOVIN_SDK_KEY` into the game's established SDK configuration.
For Flutter, an ignored JSON file consumed by `--dart-define-from-file` is suitable
when the game reads `String.fromEnvironment('APPLOVIN_SDK_KEY')`. Preserve other
existing configuration fields. Never copy the MCP `.env` or management credentials
into a game or build.

The SDK key is account-wide, shared by that AppLovin account's Android/iOS apps;
ad unit IDs are separate, platform/format-specific resources. Reuse a key from
another game only when the operator confirms the same AppLovin account. A supplied
key or successful build is not proof of account matching or working ads: check MAX
initialization and the mediation debugger on the target device. Never substitute
Management, Report, Campaign or MCP keys for the SDK key. Creating a MAX unit via
MCP does not create or return an SDK key in the current integration. If absent, ask
the operator to save it locally from AppLovin Account → General → Keys.

Keep the actual value out of instructions, Git and public skill archives. Exclude
`assets/.env.sdk.local` from ordinary distribution. An administrator-approved private
internal team copy for the same AppLovin account may include only the client
`APPLOVIN_SDK_KEY` so each employee need not retrieve it again. A different provider
account requires its own key; never distribute management/OAuth/signing credentials.
Test builds must retain their test-mode checks; a valid SDK key does not authorize
live ads or real purchases.

### Integration changes

- Fill verified IDs only in the configuration actually consumed by the existing ad wrapper/SDK. Do not duplicate initialization or replace the project's mediation stack just to match this skill.
- Preserve initialization/consent ordering, test-mode settings, release/debug separation, placement cooldowns and existing event wiring. Do not silently enable production ads in test builds.
- Add SKUs to the existing purchase catalog and bind to verified reward definitions through the established fulfillment path. Preserve receipt validation, transaction deduplication and restore behavior; never grant rewards on a client-side success flag alone or bypass server validation.
- For Unity, preserve `.meta` files/GUIDs, serialized references and imported SDK assets. Prefer supported editor tooling for binary/complex assets. Do not manufacture serialized component layouts from memory.
- If SDKs/adapters are missing, inspect the exact installed engine and package versions, consult the provider's official instructions for those versions, and propose the dependency changes before proceeding. No blind "latest" upgrade, automatic engine migration, or second competing SDK. If installation requires credentials/licensing/manual editor actions, provide the precise blocked step.

Unknown engines or abstractions are supported for investigation, not guaranteed automatic integration. If a mapping cannot be proven, leave it unmodified and say what evidence/tooling is missing. Do not emit a disconnected `.json` file and call it integration.

## 4. Verify and finish honestly

Read changed files back and inspect the scoped diff. Run the repository's available config validators/tests and a relevant compile/build if supported by the current environment. Do not switch a user's active Unity target, publish a build or launch real purchases without authorization. A test pass must name the actual command/result; if the editor/toolchain is unavailable, report that separately.

Verify these invariants from source/tests where possible:

- Correct platform IDs flow into the actual ad initialization and placements.
- For an intended release build, inspect MAX `SetTestDeviceAdvertisingIdentifiers`, Mediation Debugger test settings, Google test IDs/test-device configuration and network-specific test flags. Report each checked source and remaining device checks. Do not register every player's advertising ID as a test device. Preserve test builds and do not disable testing merely to bypass a provider readiness failure.
- Keep store linkage, AdMob approval, Pangle Live/placement state, adapter installation and device test mode separate. Wrong bidding type or missing store linkage does not by itself prove why an observed ad was a test ad. Verify MAX initialization logs (`Test Mode On`) on the actual target device; no source-only check proves every network's live delivery.
- Every newly exposed SKU matches the approved store/backend SKU and reward.
- Existing consent, receipt validation and duplicate-fulfillment protections are retained.
- No management credentials or unrelated user changes are included in the diff.

No generic local test can prove live ads or successful device IAP. Summarize provider setup, local wiring, build checks and device checks separately. Mark manual sandbox-purchase/ad-device tests pending unless actually performed. Do not make a real purchase to test the integration.

For interrupted runs, re-read provider state and the game diff before resuming. Keep a small non-secret receipt in the repository's existing task-notes location if useful: project/game IDs, platform, created/reused resource IDs, SKU outcomes, edited files and check results. It is a recovery note, not a runtime configuration file. Never commit or push unless asked.
