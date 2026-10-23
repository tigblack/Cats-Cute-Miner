---
name: superleo-monetization
description: Set up or audit a game's monetization through Superleo MCP from its repository. Check bidding, store links, provider approval and test ads; configure ad networks and IAP, Meta/Facebook, Adjust and analytics. Not for launching traffic-buying campaigns or billing/card operations.
---

# Superleo monetization

Use existing Superleo backend operations through the connected Superleo MCP. Provider credentials stay on the server. This skill supplies the workflow, not authentication or additional permissions.

## Start in the game folder

Requests such as "добавь рекламу", "давай покупки добавим" or "настрой всё для новой игры" start a guided workflow, not a request for the user to prepare API payloads. Answer in the user's language. Inspect locally first using [game-project.md](references/game-project.md); do not assume the current directory is the Superleo backend or a particular game/engine.

Read repository instructions and inspect the worktree without changing it. Discover engine, build targets, package/bundle IDs, installed monetization SDKs, configuration readers and existing purchase/reward definitions. Cite the source files for inferred values. Never equate a folder name with a store ID or use another game's remembered identifiers.

Resolve facts from the repository and panel before asking. Ask a short grouped question only for business choices or conflicting/missing evidence: which game/build target, desired ad formats/placements, child-directed treatment, purchase contents/prices and rewards. Offer a proposed plan in plain language; do not make the user supply JSON, provider IDs or endpoint names. "Add purchases" alone does not authorize invented prices or rewards. For ad setup the team standard is all six networks through the one-approval plan in [ad-setup.md](references/ad-setup.md); the user confirms that plan before anything is written.

When there is no exact Superleo project match, offer `project_create` with the verified game name, platform and identifiers; it needs the same plan/confirmation gate as other writes. Do not use Facebook onboarding to create a monetization-only project. Keep Superleo project ID, Multicast game ID, iOS bundle ID and App Store numeric ID distinct.

## Connect and establish scope

The MCP key (personal or dedicated `monetization-team`) and server URL are stored **inside the installed skill**, in `assets/.env.superleo` (also supports `assets/.env`). A private team copy can already contain the shared team key: use it without asking every employee to obtain a personal key. Check these locations before asking for credentials. Run the bundled client to load the file without exposing its contents. Paths are relative to this skill, not the game folder.

For compatibility, the client checks `assets/.env` first, then `assets/.env.superleo`, and only if both are absent the legacy game-root `.env.superleo`. It never merges credentials or falls back after an invalid/unreadable file. Never read secret files into model context or include them in repository searches, reports, commits or public skill packages. Only an administrator-approved private internal copy may include a dedicated team key, never somebody's personal key. `assets/.env.sdk.local` contains SDK settings, not the SuperLeo MCP credential.

Use `superleo_access`, `superleo_monetization_catalog`, and `superleo_monetization` through the bundled `scripts/superleo.mjs` client, or an existing MCP connection explicitly configured for the intended account/deployment. For local file setup and script commands, read [connection.md](references/connection.md). If neither method is available, local inspection and a proposed plan can continue, but remote setup is blocked. Never substitute browser clicks, direct provider credentials, or an unrelated MCP service.

Individual mode uses a personal **SuperLeo MCP** key issued by an administrator under Admin → Users → API / MCP; a configured team key does not require individual issuance. Monetization supports active, unexpired internal credential owners with roles root, dev, user, art or guest; partner/member accounts cannot own these keys. Art/guest users do not need promotion to Dev. Connect to the deployment's `/mcp` endpoint with the key in the client's secret Bearer-token configuration or the ignored local config described above; never put it in shared instructions, a prompt, shell history or report. Omit tool email: the credential binds its owner. Do not ask for another user's email to bypass access. Check deployed capabilities rather than assuming that updating the skill changes server permissions. Monetization access does not grant campaign-management, administrator or credential-management privileges.

For shared setup, the administrator issues one **Монетизация — общий ключ команды** in the same admin screen and distributes the private configured skill internally. Do not convert an existing personal key to team use. The bundled client sends the computer name automatically and optional `SUPERLEO_OPERATOR_EMAIL` from the local process environment. These are unverified audit labels, never tool `email` or proof of authorization. With `scope: monetization-team`, the returned owner email is the credential owner, not the current human: do not treat that difference as an error or request a personal key. Revocation/rotation affects everyone using the shared credential. Partners remain excluded from intended distribution; the server cannot identify a partner who obtains the shared bearer secret.

Call `superleo_access`, then the catalog. Work only if `capabilities.monetization` is true. A supported operation does not prove its provider credentials or browser service are configured.

Resolve the project and platform/package IDs through `projects`/`project`. Confirm missing or conflicting game identity, target networks, ad formats, child-directed treatment, category and store linkage. Do not silently accept provider defaults for compliance fields. "All networks" means AppLovin MAX plus AdMob, Unity, Mintegral, Pangle and Meta Audience Network, not traffic-buying networks. Meta property onboarding still has operator prerequisites; report a blocked network explicitly instead of dropping it from the request.

Read [ad-networks.md](references/ad-networks.md) for ads and [iap.md](references/iap.md) for purchases. Read both when configuring the whole game.

## Set up ads automatically

For "подключи рекламу", "заведи рекламу", "настрой рекламу", "подключи все сети" or finishing a partial setup, read
[ad-setup.md](references/ad-setup.md) and use `ads_setup_plan` → one approval of the shown plan →
`ads_setup_apply` → `ads_approval_status`. The plan covers MAX, AdMob, Unity, Mintegral, Pangle and
Meta Audience Network (including Meta property setup and the Play listing developer website), reuses
existing resources and halts only the failing network/format. Per-network operations are for repairs.

## Audit an existing game's ads

For "что с рекламой у X", "проверь рекламу", "почему тестовая реклама" or a release-readiness check,
read [ad-readiness.md](references/ad-readiness.md) and [ad-networks.md](references/ad-networks.md).
Start a read-only audit of the exact game/platform, not creation or a migration. First call
`ads_approval_status` and `ads_setup_plan` once for the game (`params: {id: <Superleo project ID>}`,
read-only; never apply the plan in an audit); they usually answer the whole question. Then check configured
MAX networks, their exact bidding units, store links and approval/Live state, then local SDK IDs
and test-mode configuration when source/device evidence is available. Do not answer solely from
saved project IDs or the panel's green configuration status.

Lead with **🔴 РЕКЛАМА НЕ ГОТОВА К РЕЛИЗУ** when required provider setup is blocked; show each
missing condition and the concrete next action. Use **🔴 ВКЛЮЧЁН ТЕСТОВЫЙ РЕЖИМ** only with
specific provider/SDK/device evidence. Missing links or wrong bidding units alone do not prove
that an observed impression is a test ad. Unknown checks must remain **🟡 НЕ ПРОВЕРЕНО**, not green.
Always include **«Что нужно поправить»** for failures: exact resource, observed versus required
setting, where/how to fix it, whether the skill can do it, and how the result will be verified.
For unknown checks, instead name the evidence/access needed; do not invent a configuration defect.
The user asking to inspect authorizes read-only verification, not changes; propose a bounded repair
plan and ask for approval only for writes not already authorized.

For Meta/Facebook bidding, Audience Network placements or ads served inside the game, read [meta-audience.md](references/meta-audience.md). Use `meta_audience_*` operations and `FACEBOOK_NETWORK` in MAX; developer App ID and Page are not Placement IDs. Android property setup (property, app, payout with a single account, app-ads.txt review, MAX mediation, ad space) and placements are automated; several payout accounts, a non-public listing and Meta's own review remain operator/provider steps.

For "создай приложение в ФБ", developer-app SDK setup or binding an existing Meta app to an ad account, read [facebook.md](references/facebook.md). Use the catalog's `facebook_*` operations through the same client/key, not the older campaign-onboarding tool. This separate setup does not configure Audience Network, campaigns, ad accounts, Pages or Instagram accounts. A monetization-only request does not implicitly authorize developer-app creation. Resolve ambiguous "подключи Meta" from context or ask whether the user means in-game monetization or developer-app/event integration.

When the user also requests AppMetrica analytics, read [appmetrica.md](references/appmetrica.md) for discovery, explicit privacy choices, creation, project binding and client SDK configuration. This is optional analytics setup, not an additional mediation network.

For Adjust app creation or linking an existing attribution app, read [adjust.md](references/adjust.md). Use `adjust_*` operations through the same connection. This is optional attribution setup, not another mediation network: do not add it to an ads/IAP-only request. A returned ticket or app token alone is not completion; verify the ticket and app before binding the project.

For "заведи стандартные события", "настрой события Adjust" or event/cohort/parameter analytics, read [adjust-events.md](references/adjust-events.md). Use `adjust_event_standards`, `adjust_events`, `adjust_create_standard_events`, and `adjust_events_report`. Create only for the requested, verified project; never sweep all games automatically. Distinguish provider definitions, SDK token/parameter wiring, callback delivery and observed dashboard data. No provider keys or callback secrets belong in the game or skill.

For nested/dynamic game JSON, migrating AppMetrica event shapes, A/B conversion/statistics, retention, activity or funnels, also read [adjust-game-events.md](references/adjust-game-events.md). The opt-in `game-events-v1` channel preserves logical names and bounded nested payloads behind one per-app Adjust token. Keep provider `event_name` separate from `game_event_name`; do not silently replace standard/revenue tracking. Extended reports require `analytics="true"`; distinguish assigned users from event senders, honor sample/coverage warnings, and never equate observed significance with randomized causality.

For local MAX SDK initialization, check `assets/.env.sdk.local` for the operator-provided `APPLOVIN_SDK_KEY` using a local parser without printing its value. This is separate from the MCP connection file and is not loaded by `superleo.mjs`. Follow the SDK-key handling in [game-project.md](references/game-project.md); do not ask again for a configured key or confuse it with an Ad Unit ID.

Create AdMob apps through `admob_create_app`. For MAX, use `admob_create_unit` with `servingType: partner_bidding`: the backend uses the AdMob browser worker because the public API does not document Partner bidding creation/type verification. API provisioning permission alone does not establish that capability. Never substitute a standard unit or silently fall back when the worker is unavailable. Follow [ad-networks.md](references/ad-networks.md) for existing-unit verification and store linking.

For AdMob and Pangle, resolve the actual public store listing before creation. A package ID, generated URL, internal-test invitation, successful create response or enabled MAX network is not production readiness. Missing store links require an explicitly approved unpublished/test setup and an unfinished result, not an automatic default. Check provider approval/Live state separately from local SDK test mode; do not turn test ads off as a workaround for unverified provider configuration.

Before creating Google Play IAP through Superleo, follow the billing-build prerequisite in [iap.md](references/iap.md): confirm that this app has a billing-enabled build uploaded and released to a Play track (internal testing is sufficient). If missing or unknown, proactively offer to check/integrate billing, prepare an AAB and help with upload. Do not assume local SDK presence proves Play readiness or that a monetization request authorizes a release.

For EU/EEA consent (GDPR), ads or analytics disabled in Europe, or a "Manage privacy" option, read [consent.md](references/consent.md): MAX Terms and Privacy Policy Flow with Google UMP, the AdMob GDPR message (plan step `admob:gdpr`) and the game changes.

For publishing to Google Play production ("выложи в стор", or ad setup blocked by a missing public listing), read [play-publishing.md](references/play-publishing.md): store texts/graphics, developer website, Data safety and the production release via the Play Developer API, with the App content forms that still need an operator.

For a new Play Console app or first AAB, read [play-onboarding.md](references/play-onboarding.md). It covers the complete guided preparation: existing game or explicitly requested Flutter smoke app, billing integration, upload-key handling, signed AAB validation, staging, MCP draft upload/adoption, existing tester-list selection and separately confirmed internal release. Perform authorized local preparation with the game's tooling; `play_*` operations do not build apps or generate keys. File transfer requires an available authorized transport; signing enrollment and license testing remain operator steps. Use the MCP tester-list operations instead of asking a user without Console access to click through it. Never report IAP ready just because creation/upload/release succeeded.

## Plan, execute, verify

All `superleo_monetization` calls select a catalog `operation`. Use `params` only for path IDs, `query` for reads, and `body` for that operation's native payload. Do not invent HTTP paths.

1. Read current apps/units/products and reuse exact matches by platform + package + format/SKU. An empty list with an error/message is a failed read, not proof of absence. Stop ambiguous matching for user selection.
2. Assemble a bounded plan: project/game, networks, formats, existing versus new resources, privacy choices, SKU prices/rewards and intended activation. Write calls without `confirm` return a local plan only (`providerValidated: false`), not a provider dry run. Use them to check permission and payload shape; backend/provider validation still happens on execution.
3. Obtain authorization for the concrete plan before sending `confirm: true`. An existing explicit authorization for the same plan can cover its dependent steps; do not require redundant approval for each ID. Changing game, network set, price or compliance choice requires renewed confirmation. IAP import activates Google Play purchase options, not just drafts.
4. Execute dependency order: existing/create Superleo project → provider apps → provider units/placements → MAX mediation → project network IDs. For IAP: verify Google Play billing-build readiness → existing/create Multicast game → product import. If the prerequisite is missing, offer the separately approved build/upload workflow before Google Play writes. Do not automatically start traffic-buying campaigns or modify OAuth/account credentials.
5. Check both transport success and the returned backend `success`/per-item results. Save returned IDs in the task record without credentials. Poll/read the specific resulting resources before claiming completion. Return an explicit per-network and per-SKU outcome, including blocked, partially applied and unknown states.

Never blindly retry a timed-out/failed mutation. Read back existing state first: a response can fail after the provider has committed. Reuse the existing resource or stop with its IDs and recovery action. There is no global transaction or rollback across providers. Do not delete resources as rollback.

After verified remote setup, follow the local integration and verification steps in [game-project.md](references/game-project.md). A request to add monetization in this game permits scoped local config/code edits, not unrelated refactors, store publication, production purchases or automatic git commits.

Do not print SDK/private keys, OAuth tokens, or raw provider configuration containing credentials. Report only identifiers and verified configuration. Distinguish four outcomes: provider resources, local wiring, build/tests, and device verification. Backend success alone is never "the game is ready".
