# Reward-button launch — 23 October 2026

The Store ad chest and offline Double Income previously called `TryOpenRewardedAdsChoices`, which opened the IMGUI Analytics & privacy screen over the Store. Saving that form did not resume the original reward action. A player with an established protected age could not enable the additional service from that form.

`OutpostHud.Rewarded.cs` now coordinates all three reward entry points: Store chest, Double Income and periodic offers. When MAX already has a ready ad, the existing durable offered → earned → saved reward path runs immediately. Otherwise a pixel-kit dialog prepares the ad, waits for native SDK/CMP initialization and loading, then invokes the original action once. Cancel, a changed run, a destroyed provider or an expired source context discards the request. Loading can be cancelled and times out after 30 seconds of foreground loading; time spent in the native consent flow is excluded. Retry does not grant a reward.

The reward tap durably opts into Ads only. Unknown age requires an explicit 16+ choice in the pixel dialog. An established protected age is never rewritten or silently promoted. Existing analytics choices and explicit AppMetrica opt-out stay independent. The complete analytics/settings screen remains available only through Settings. Native Google UMP still owns regional ad consent; no binary consent grant or sample App ID is used.

The MAX initialization callback previously required a set binary consent flag in every region. It now requires supported CMP and known geography, requiring the consent-set flag in GDPR regions only. Known non-GDPR users can load ads without a form that is not required there. Existing-user CMP choices reevaluate readiness; a failed consent setup can be retried through the native CMP in GDPR regions.

All reward action buttons use the shared gold style from Double Income. Store ad tiles remain tappable while an ad is being prepared and show the TV/Open action; cooldowns retain their disabled presentation.

## Validation

Android-target PlayMode `build/ci-logs/PlayMode-20261023-153144.xml`: **71 passed, 0 failed, 1 ignored**, 3.74 seconds. The ignored test is the historical release-without-ads scenario. Coverage includes explicit unknown-age selection, established protected age, save failure, no additional analytics opt-in, regional CMP readiness, pixel modal cancellation/stale context, ad chest/cooldown, Damage UI and durable earned-reward/background/save-retry delivery. No full level/campaign runs. A read-only independent source review found no remaining blockers.

The prior run had one new UI test failure because its fake HUD had not yet established its initial run cache before simulating a tap; the test now waits for the initial frame. A compile-only run also identified an incorrect namespace qualifier, corrected before the passing run.

The owner clarified that they had never chosen an age. Startup regression run `PlayMode-20261023-153810.xml` passed **14/14**, including fresh installation, empty/unknown legacy settings and unfinished age selection. These states remain Unknown and can explicitly confirm16+ in the reward flow; no age or ad consent is inferred from a tap. The specific saved value on the owner's phone has not been inspected, so the screenshot alone is not proof of an explicit child-age choice.

The AdMob app/project mapping and actual App ID remain as recorded for APK0.1.19. A fresh backend read again returned `ADMOB_GDPR_UNVERIFIED`, retryable=false. Published GDPR-message membership and real-device ad fill/callbacks remain unverified; source tests and artifact checks do not establish these. The existing server limitation must be resolved before claiming GDPR delivery readiness.

APK **0.1.20 (10)** is built at `Builds/Android/CatMiners-0.1.20-rewarded.apk`. `Tools/GooglePlay/build-install-apk-macos.py` verified its signature, Unity receipt/hash, package/version, nondevelopment configuration, MAX/Google UMP/Billing DEX classes, own AdMob manifest ID, native consent configuration and ARM64/ZIP16KB alignment. Reports: `Docs/Reports/android-rewarded-launch-20261023/verified-apk.json` and `gameplay-validation.json`. The signing certificate is compared separately against APK0.1.19; actual phone installation and ad delivery remain device checks. No Google Play publication is part of this fix.

The initial build command did not execute because the Mac refused the hub login (`Connection timed out during banner exchange`). The connection subsequently recovered without connection configuration changes or local build fallbacks, and the guarded build completed normally on the Mac at low priority.
