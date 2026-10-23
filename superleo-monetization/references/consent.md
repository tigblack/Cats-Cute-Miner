# User consent (GDPR / EEA, UK, Switzerland) for MAX games

Use when setting up or auditing ads in a game shipped in Europe, or when ads/analytics are disabled for
EU users. The studio standard is **MAX's built-in Terms and Privacy Policy Flow with Google UMP**: MAX shows
Google's GDPR form in GDPR regions, reads the TCF consent strings and passes them to every mediated network.
No separate UMP SDK integration is needed. Sources: AppLovin
[Terms and Privacy Policy Flow (Unity)](https://support.applovin.com/en/max/unity/overview/terms-and-privacy-policy-flow),
Google [Disclose to EEA users](https://developers.google.com/admob/unity/privacy/gdpr).

## Workflow for one game

For "включи рекламу в ЕС", "GDPR", "согласие" or ads/analytics disabled in Europe, work on the user's own game only:
1. `ads_approval_status` (`params: {id}`): read `consent.state`.
2. `missing` → `ads_setup_plan`; it contains step `admob:gdpr`. Show the plan, get one approval,
   `ads_setup_apply`, then `ads_approval_status` again: expect `consent.state = included`.
   `no_message` or a GDPR blocker in the plan → the operator action from section 1; do not work around it.
3. `included` → the AdMob side is done; continue with the game changes (section 2) and verification (section 3).
   EU ads stay off until the new build with the MAX flow is released.

## 1. AdMob account (Privacy & messaging) — automated in the ad setup plan

The Google form can only show after the game's AdMob app is in a **published GDPR message**. The AdMob API
has no Privacy & messaging endpoints; the backend uses the AdMob browser worker:

- `admob_gdpr_status`, `query: {appId}` (read-only): the app's privacy policy URL, the account's GDPR
  messages, `inPublishedMessage`, `studioPrivacyUrl` (the single URL used by apps already in the message),
  and `partners.missing` among Google, AppLovin, Unity, Mintegral, Pangle, Meta.
  `ads_approval_status` reports it as `consent.state` = `included | missing | no_message |
  not_configured | unknown`.
- The ad setup plan adds step **`admob:gdpr`** (`admob_gdpr_add_app`, `body: {appId}`) when the app is
  not in the message, approved with the rest of the plan. It sets the studio privacy URL on the app when
  the app has none (an app without a URL cannot be selected), ticks the app in the single published
  message and confirms. The message stays published; texts, partners and other apps are not touched.
  A failure halts only this step; `ADMOB_GDPR_UNCERTAIN` means re-read the status, never retry blindly.

Blockers that need a person (the plan reports them, no step is created):
- `ADMOB_GDPR_MESSAGE_MISSING` — no published message. Create one: Privacy & messaging → GDPR (newer UI:
  "European regulations") → **Create message**: the app(s), languages; User consent options **Consent or
  Manage options** ("Close (do not consent)" unchecked); Targeting **Everywhere**; Styling: global
  secondary color `#ffffff`, buttons secondary color `#6e6e6e`; **Publish**.
- `ADMOB_GDPR_MESSAGE_AMBIGUOUS` — several published messages: the operator picks one.
- `ADMOB_GDPR_PRIVACY_URL_UNKNOWN` — message apps use different policy URLs: set the game's URL manually.
- `ADMOB_GDPR_PARTNERS_MISSING` — GDPR settings → **Review your ad partners** (✎): select the listed
  networks, Confirm, Save. Missing partners make UMP deny consent for them and reduce fill.

Live state (2026-10-21): one published message ("Untitled GDPR message"), all six studio networks selected,
studio URL `https://multicastgames.com/policy`.

## 2. Game (Unity) — skill can edit the project

1. **AppLovin → Integration Manager → Privacy Settings**: tick **Enable MAX Terms and Privacy Policy Flow**;
   **Privacy Policy URL** (studio: `https://multicastgames.com/policy`), optional **Terms of Service URL**
   (`https://multicastgames.com/termsofuse`), keep the default User Tracking Usage Description (iOS).
   Leave "Show Terms and Privacy Policy Flow when in GDPR Regions" off.
2. Load ads only **after** `MaxSdk.InitializeSdk` completes (`OnSdkInitializedEvent`): MAX shows the
   consent form first. Remove game code that forces ads/analytics off for EU users when this flow is used.
3. Settings screen: "Manage privacy" button, shown when
   `MaxSdk.GetSdkConfiguration().ConsentFlowUserGeography == ConsentFlowUserGeography.Gdpr` (as in AppLovin's docs),
   calling `MaxSdk.CmpService.ShowCmpForExistingUser(error => { ... })`.
4. Analytics/attribution SDKs (AppMetrica, Adjust) must respect the same consent: start or enable them
   after initialization, using the TCF/consent state the game already handles; do not invent a second form.
5. MAX's binary consent flags (`SetHasUserConsent`) are for games without a CMP; with this flow the
   consent comes from UMP, so do not add them on top.

## 3. Verify

- Debug build: Integration Manager **Debug User Geography = GDPR**; before init call
  `MaxSdk.SetExtraParameter("google_test_device_hashed_id", "<hash from the Google CMP log>")`.
- The form shows only to new users: reinstall to repeat. Expect: form → choice → init callback → ads load.
- In MAX Mediation Debugger check that networks receive consent. Release builds must not keep the debug
  geography or test device ID.
- Report the AdMob message as published/missing and the game flow as built/verified-on-device separately.
  Without a published GDPR message, EU users see no Google form and the game must keep ads off in the EU.
