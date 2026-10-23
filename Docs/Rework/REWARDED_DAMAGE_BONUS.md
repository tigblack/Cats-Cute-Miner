# Rewarded Damage ×2 bonus

`RewardedBonusKind.Damage` is appended as enum value 4. Its durable reward ID is `damage`, its ad placement is `bonus_damage`, and its quoted amount is 1. Existing reward values and weights are preserved; Damage has weight 20 in both `store_catalog.json` and the embedded default catalog.

`RewardedBonus.DamageDurationSeconds = 300` and `DamageMultiplier = 2` define the fixed reward. `TeamStats.FinalDamage` includes the multiplier, so cat hits (including target/card bonuses and critical hits) and player pickaxe hits use it. Coin value, goods production/prices, currencies and percentage-based dynamite damage do not use the multiplier.

The Android claim follows the existing offered → earned callback → staged delivery → durable commit pipeline. Claiming or closing an ad does not grant the boost. `DeliverVerifiedReward` rejects unearned/mismatched/replayed receipts and now plans successful deliveries immediately, keeping cached stats consistent with ordinary commands. The existing explicit Editor reward simulation still supports this bonus for tests.

## Timing and saves

`OutpostProfile.DamageBonusSecondsLeft` persists an exact `Rational` number of simulated seconds. Old saves omit the field and read zero. This profile field survives mine transitions even though a new run resets its clock; it is independent of a mine's tick rate. Save invariants require a value between zero and 300.

`OutpostSimulation.DamageBonusActive` exposes activity. Expiry is included in `NextEventTick`; time is consumed before each event and at the final `Advance` boundary. At expiry, stats and target damage caches are rebuilt **before** movement/hits/commands on that tick. The active interval is `[grant, expiry)`. Offline jobs, event-driven advance, limited batches, reloads and time skips use the same code. Only simulated elapsed time is consumed; the clock and duration never use wall-clock timestamps.

Damage is excluded from weighted offers while active. A distinct already-earned receipt arriving while active refreshes the duration to 300 seconds; it never adds duration or produces ×4. A replayed receipt cannot refresh the timer.

Runtime delivery first commits the earned receipt, including while suspended or during an offline job. This receipt-only write marks the live offer and the in-flight job's offer earned without replacing the simulation or rebasing its clock. Delivery waits for both focus/pause suspension and offline processing to finish, then calls `AdvanceOnline` before preparing the grant. Unreported gaps enter ordinary offline catch-up; time spent processing catch-up also advances before the reward. A reloaded earned receipt follows the same boundary. All pre-grant hits remain unboosted and a newly delivered Damage reward starts with the full 300 seconds. Callback removal/provider acknowledgement still happen only after the atomic grant/receipt commit succeeds; save retries retain their immutable prepared snapshot. Ordinary Editor previews retain the existing explicit `SimulateRewardedOffersForTests` guard.

## HUD and explicit ad choices

Owner revision, 23 October: all reward-ad actions share `PixelUi.RewardedButtonStyle`, the same gold style as Double Income. This includes ready ad-chest tiles and periodic bonuses of every kind; ad-choice entry tiles use it too. Store titles/action labels use the matching dark text without a shadow on gold. Cooldown tiles retain their unavailable presentation. The existing chest/cooldown and Damage HUD PlayMode checks passed (2/2, Android target, `PlayMode-20261023-143414.xml`). This source revision follows APK0.1.19 and is not included in that already-built artifact.

The periodic offer reuses `icon_pickaxe`, with “Damage ×2 / 5 min” beside it. A separate noninteractive pixel-kit badge shows the pickaxe and `×2 mm:ss` during the boost. Existing offers can appear and be claimed beside that badge. Windows hide both indicators without pausing simulation.

`OutpostHud.DamageBonusVisible` and `DamageBonusLabel` expose presentation for focused tests. There is no new art or `IOutpostGame` requirement.

Settings retains “Analytics & privacy” and adds “Rewarded ads” when `GameSdkConfiguration.AdsEnabled`. It calls the parent's `NeutralAgeFlow.OpenChoices(gameObject, true)`. An unavailable ad-chest tile with missing consent becomes a chooser entry; offline-double also opens the chooser before attempting a claim. These actions never grant rewards or invent consent. Existing cooldowns and command restrictions still apply.

## Changed implementation files

- Core: `Outpost/Store/RewardedBonus.cs`, `Outpost/Config/StoreCatalog.cs`, `Outpost/Config/OutpostConfigSet.Defaults.cs`.
- Core simulation: `Outpost/Sim/OutpostState.cs`, `TeamStats.cs`, `OutpostSimulation.cs`, `OutpostSimulation.Tick.cs`, `OutpostSimulation.Bonus.cs`, `OutpostSimulation.Store.cs`, `OutpostSimulation.Commands.cs`, `OutpostInvariants.cs`.
- Text/catalog: `Assets/Game/Scripts/Core/Text/Loc.Outpost.cs`, `Assets/Game/Resources/Outpost/store_catalog.json`.
- Runtime: `OutpostController.Bonus.cs`, `UI/OutpostHud.Bonus.cs`, `OutpostHud.Settings.cs`, `OutpostHud.Results.cs`, `OutpostHud.Store.cs`, `StoreTileView.cs` under `Assets/Game/Scripts/Runtime/Outpost`.
- Tests: new `EditMode/Outpost/OutpostDamageBonusTests.cs` and deterministic `.meta`; extended `OutpostRewardedBonusTests.cs`, `PlayMode/Outpost/OutpostBonusUiTests.cs`; platform applicability corrected in `PlayMode/IntegrationTelemetryTests.cs` and `PlayMode/Outpost/OutpostStoreUiTests.cs` under `Assets/Game/Tests`.

## Focused validation

Run only on the Mac, without package restore or Unity:

```sh
on-remote nice -n15 /Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet test Tools/CoreTests/Tests/Tests.csproj --no-restore --nologo --filter 'FullyQualifiedName~OutpostDamageBonusTests|FullyQualifiedName~OutpostRewardedBonusTests|FullyQualifiedName~IntegrationPrivacyRewardTests'
```

The damage tests use one synthetic high-HP rock and isolated transition states, without full mine/campaign runs. They cover weighted availability, rejected unearned/wrong-run rewards, immediate damage stats, cat and player hits exactly before/at/after expiry, long/partitioned/offline advancement, partial tick limits, time skips, save/load, failed delivery commits, receipt replay, old saves, mine transitions, and nonstacking refresh. Existing currency and dynamite assertions run with and without the boost.

Verified on the Mac: **49 passed, 0 failed, 0 skipped** using the exact .NET filter above, `--no-restore`, and `nice -n15`.

Parent-coordinated PlayMode candidates: `CatMiners.Tests.OutpostBonusUiTests` (or just `Damage_OfferExplainsDuration_ActiveCountdownAllowsOtherOffers_ThenExpires` and `RewardedAds_SettingsShortcutMatchesProviderSupport`), plus the corrected platform tests `ClientResourceContainsPublishedDocumentsAndMatchesPlatformAdvertisingSupport` and `OrdinaryEditor_NoAdOffersOrRewardShortcuts_PaidStoreRemains`.

The runtime boundary regression filter is `^CatMiners\.Tests\.OutpostRuntimeTests\.RewardDelivery_`. It selects nine checks in `Assets/Game/Tests/PlayMode/Outpost/OutpostRuntimeTests.Rewards.cs`, using the real controller, receipt callback set, save service and sliced offline coordinator with a synthetic single high-HP rock and a manual clock. Coverage: immediate durable background receipt, combined focus/pause, ordinary frames and unreported gaps, reload without the callback, partial catch-up/resuspension/processing time, receipt and offline-save retry delays, and atomic grant retry/replay. These checks compare pre-reward target HP with an independent unboosted simulation and assert the full 300 seconds at delivery. No full mine or campaign runs are involved.

Boundary-fix verification on the Mac (`on-remote nice -n15`): the focused core filter above passed **49/49** again, and `Tools/UnityCheck/PlayModeTests/PlayModeTests.csproj` compiled Runtime and PlayMode code with **0 warnings, 0 errors**, using existing Unity assemblies and `--no-restore`. The nine new PlayMode checks passed in the coordinated Unity runs recorded below.

The delegated gameplay implementation was followed by the parent-coordinated native integration, targeted Unity runs, visual checks and APK verification below. Real-device provider callbacks and consent remain unverified.

Parent verification: Android-target PlayMode `PlayMode-20261022-211609.xml`:57passed,0failed,1ignored (historical no-ads-only runtime scenario),5.97seconds. Native MAX preparation gate passed before packaging. Phone-ratio HUD/offer/countdown/settings screenshots are under `Docs/Reports/first-session/damage-bonus-*.png` and `rewarded-ads-settings.png`; no truncated text. Ordinary Android Editor preview cannot grant simulated rewarded items unless `SimulateRewardedOffersForTests` is explicitly set. Device ad delivery remains unverified.

Post-review verification: Android PlayMode `PlayMode-20261022-213104.xml`:17/17passed in7.03seconds, including9new suspended/offline/save-retry coordinator regressions. The earned receipt is persisted before waiting, but the boost starts only after catch-up.

Final Android-target PlayMode run `PlayMode-20261023-034632.xml` passed **65 checks, 0 failed, 1 ignored** in6.89seconds after provisioning the own AdMob app and adding the existing-user privacy controls. The ignored scenario applies to the historical release without ads. These are targeted checks without full mine runs.

APK **0.1.19 (9)** is built at `Builds/Android/CatMiners-0.1.19-rewarded.apk` with the existing Android local install key, IL2CPP ARMv7+ARM64, targetAPI36 and no development mode. Its signature, build-receipt hash, manifest, AD_ID permission, actual MAX/UMP/Billing DEX classes, native consent configuration and 16KB ARM64/ZIP alignment passed artifact verification. Android resource optimization renames the MAX JSON entry; the verifier resolves `raw/applovin_settings` from the actual resource table rather than assuming a ZIP path. `--verify-only` repeats artifact checks without launching Unity. Evidence: `Docs/Reports/android-rewarded-20261022/verified-apk.json` and `gameplay-validation.json`.

The own AdMob Android app was created and read-back verified through `superleo-monetization/SKILL.md`, mapped to the exact Superleo project and saved into the runtime configuration. It is an unpublished app with store linkage and approval pending. The existing MAX REWARD unit matches the package. GDPR membership is **unknown**: the service returns `ADMOB_GDPR_UNVERIFIED`, and its browser worker reports support only for app/ad-unit creation and store linking. The worker must support GDPR operations before published-message membership can be checked and, if missing, added. This external limitation does not prevent installing the APK; real-device ad fill, consent and callbacks have not been verified. No sample App ID, forced consent or debug geography is used. Provisioning evidence: `admob-provisioning.json` and `max-unit-readback.json` in the same report folder. This APK has not been published to Google Play.
