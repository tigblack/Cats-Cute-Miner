# Audit ads for an existing game

Trigger: “Что с рекламой у X?”, “Проверь биддинги и ссылки”, “Почему тестовая реклама?”, or release readiness.
This is investigation, not permission to change provider settings or start campaigns.

## Identify and inspect

1. Use the configured skill-local key and `superleo_access`, then catalog. Do not ask an employee
   for network credentials or a new personal key when the dedicated team credential works.
2. Find the exact project with `projects` / `project` and confirm target platform, Android package
   or iOS bundle **and numeric App Store ID**. Resolve conflicting identities before provider calls.
3. **Start with the two whole-game reads** (both read-only, `params: {id: "<Superleo project ID>"}`,
   called once for the game, never per network):
   - `ads_approval_status`: each network's review/approval state, `blocksMax`, what is already in
     MAX (`connectedToMax`) and remaining `pendingSteps`;
   - `ads_setup_plan` (with `query.formats`, a comma string such as `"REWARD,INTER"`, only if the game has
     no MAX units yet): every missing resource or hookup as a step, plus `blockers`. Do **not** apply it in
     an audit; offer it as the repair plan.
   These two answers are usually the whole audit. Go into the per-network reads below only to explain a
   specific step/blocker, or when the user asks about bidding type, test ads or a network the plan does not
   cover. Use `project_integrations` with params `{id}` for the panel checklist, if deployed. Use
   `provisioning_health` only when a browser-dependent check is needed. These are read-only;
   a healthy worker/tunnel does not prove browser login, access, bidding or ad delivery.
   If the new operation is absent, say the backend needs updating and continue with supported reads.
4. Read `max_units` for exact package/platform and `max_unit` for each relevant unit, sequentially.
   Inspect all formats used by the game, not just rewarded. Match MAX placement IDs to the actual
   provider records below. Never dump full MAX settings: they can contain credentials.
5. Use the matrix below. Read failures, paginated/incomplete lists, unavailable workers and unknown
   fields mean **not verified**, not “no app” and not “ready”. Bound reads to this game's resources;
   do not fan out across every game in the account or continuously poll.

| Network | Read and compare | A failing check means / recovery |
| --- | --- | --- |
| MAX | Package/bundle, platform, format, disabled state, expected networks, exact app/unit mappings | Wrong/missing/disabled mapping requires a targeted repair; preserve other networks and the MAX unit ID |
| AdMob | `admob_apps`: exact App ID, platform, `linkedAppInfo.appStoreId`, `appApprovalState=APPROVED`; `admob_units` and `admob_verify_bidding` for every MAX-bound ID: actual saved Partner bidding, app and format | Missing store: `admob_link_store`; review pending: wait/resolve provider review; standard unit: propose separate Partner bidding unit and change only that MAX mapping, never convert/delete the old unit blindly |
| Pangle | `pangle_apps`: exact app, `download_url` for this store/platform, `status=2`; `pangle_placements`: exact slot, owning app, format, `bidding_type=1`, `status=2` | Missing URL/Test: propose `pangle_link_store` with freshly read expected guards; status 1: verification pending, no retry; paused/test/wrong placement: explain and obtain approval for a supported repair |
| Unity | `unity_project`: correct project/store Game ID, `adsProvider=max`, store identity/linkage when returned; `unity_units` and `unity_placements` (query `{projectId, store, adUnitId}`): exact MAX placement, format and returned `placementType=bidding` | Offer `unity_set_mediation` / appropriate bidding placement after approval; missing API evidence of force-test mode stays unknown, never infer OFF |
| Mintegral | `mintegral_apps`: package, OS, `store_url`, `is_live_in_store`, mediator; `mintegral_placements`: owning app/format; `mintegral_units` (query `{placement_id}`): exact MAX unit and returned bidding type | A placement list omits unit details: use the unit read. If type is still absent, do not infer HB from the MAX key or name; mark unknown and identify required provider evidence, without creating a replacement to “check” it |
| Meta Audience Network | [meta-audience.md](meta-audience.md): `meta_audience_status` then fresh `meta_audience_inspect` (or property discovery); exact store/platform, property/ad space, placement format, review/payout/test mode; compare the placement with enabled `FACEBOOK_NETWORK` in each relevant MAX unit | Offer supported binding/placement/MAX repairs; missing property/platform/ad space/store setup, payout or review are explicit operator/provider dependencies. Developer App ID/Page is not a placement. Unknown browser evidence stays unknown; do not invent a Meta Partner-bidding checkbox |

`admob_verify_bidding` is a POST-based **inspection**, not creation. This is the one exception to the
general "approval before `confirm:true`" rule: if its transport requires `confirm:true`, it is covered
by the user's explicit audit request; explain that it only reads
provider settings and refreshes a verification receipt. This does not authorize any link/create/update
operation. Verification receipts expire from the selectable list after 15 minutes. No receipt proves
app approval or live delivery, and MAX enablement rechecks the saved unit.

A syntactically generated store URL is not proof the app is publicly listed. Check the official listing
when accessible. Internal-track opt-in links are not public store listings. If the store is unavailable
or regional availability cannot be established, report the limitation instead of “app does not exist”.

## Test ads and the actual build

When the chat is in the game repository, follow `game-project.md`. Trace the intended release target:
real MAX/ad-network IDs, installed adapters, test sample IDs, `SetTestDeviceAdvertisingIdentifiers`,
Mediation Debugger testing and network-specific test flags. Do not print the SDK key.

Runtime settings can differ from source. With authorized device logs check MAX `Test Mode On`, selected
test networks and actual loaded-ad network. Without device evidence explicitly leave device verification
pending. Never click production ads, request real revenue traffic or disable test protection in a debug
build just to diagnose. Consent, fill, store review, adapters and region can independently prevent serving.

**Do not claim “будет тестовая реклама” from a missing link or standard unit alone.** These can prevent
live demand/approval or give no fill; confirmed test mode is separate. A known Pangle Test app/placement
is a reason to flag that network's test configuration, not proof all MAX impressions are tests.

## User-facing report and repair

Lead with one clear headline, not a vague “action required”:

- **🔴 РЕКЛАМА НЕ ГОТОВА К РЕЛИЗУ** — known required setup failures; list what is wrong and what to do.
- **🔴 ВКЛЮЧЁН ТЕСТОВЫЙ РЕЖИМ** — identify network/build/device and the exact supporting evidence.
- **🟡 ГОТОВНОСТЬ НЕ ПОДТВЕРЖДЕНА** — required checks could not be completed; name the blocker.
- **✅ НАСТРОЙКИ ПРОВЕРЕНЫ; ПОКАЗ НА УСТРОЙСТВЕ …** — only when actual provider checks passed;
  separately say device verified/pending. Never promise revenue or fill.

Use a short per-network table: network, bidding, store, approval/Live, test-mode evidence, next step.
List only this game's intended networks; an intentionally unused network is not a defect. Do not silently
omit a requested network. Give check time and relevant non-secret IDs. Example conclusion:

> 🔴 РЕКЛАМА НЕ ГОТОВА К РЕЛИЗУ: AdMob не привязан к магазину, блок Rewarded не Partner bidding;
> Pangle — Test без store URL. Это не доказывает причину тестового показа в билде.
> Предлагаю привязать оба существующих приложения, создать правильный AdMob-блок и заменить
> только его соответствие в MAX. После одобрения сетей отдельно проверить test mode релизной сборки.

Follow the summary with **«Что нужно поправить»**, ordered by dependencies. For each confirmed
failure give the exact game/platform, network and app/unit ID (or release source file), the observed
setting versus the required setting, and the concrete change. State where it is applied and who
can do it: **«Могу исправить через скилл после подтверждения»**, **«Нужно действие человека»**,
or **«Ожидаем проверку провайдера»**. Claim automation only when the deployed catalog supports
that operation and its prerequisites are available. Use a verified panel location/provider link
or repository path; never invent a console URL. Explain the change in plain language, not just
an operation name or “action required”. Include the readback/device evidence needed to close it.

For example, a confirmed standard Rewarded unit requires a new Partner bidding unit and replacement
of only that network mapping in the existing MAX unit; identify the old unit and preserve other
networks. A missing store link requires the verified listing URL, then a separate approval check;
linking alone is not completion. Do not instruct recreation of the whole provider app.

Put unavailable/unknown checks under **«Что ещё нужно проверить»**, with the exact missing evidence
or dependency and the next check, not an unproven repair. Finish with a bounded offer to apply the
supported fixes together; a diagnostic request alone does not authorize execution.

After approved repairs, reread exact resources and repeat the audit. Pending review stays pending.
No repeated create after ambiguous outcomes, no deletion as rollback, no automatic whole-account migration.
