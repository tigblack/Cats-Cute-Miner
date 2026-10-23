# New Play Console app and first billing AAB

Use these named `superleo_monetization` operations only after the deployed catalog
exposes them. They reuse the existing Play Console browser worker, not Google's
Publishing API. Require an authenticated English-language Console browser. Check
`play_browser_health` first; a healthy worker does not prove Console login.
Check its `onboarding.commands` too: a missing list means the browser worker needs
updating even if the backend catalog already lists the operations. `aabToolsConfigured`
only checks that configuration is present, not that Java/bundletool actually work.

All onboarding operations currently use POST, including inspections: with
`confirm` omitted they return only the local plan. Use `confirm: true` to actually
inspect after the user approves opening the form. Do not confuse the outer MCP
confirmation with approval of legal declarations or a release.

## 1. Inspect the create form

`play_inspect_app_form` body:

```json
{
  "projectId": "project-id",
  "app": "com.example.game",
  "devId": "123456789",
  "name": "My Game",
  "language": "English (United States) – en-US",
  "appType": "game",
  "pricing": "free",
  "contactEmail": "support@example.com"
}
```

Values are illustrative: discover the actual project/package/developer account,
ask for business choices and use the exact language option label shown in Console.
`appType` is `app|game`, `pricing` is `free|paid`. Do not infer these choices.
The project must already exist and its `androidPackageName` must match `app`.
Inspect existing apps before requesting creation. A same-title candidate needs
identity review, not automatic reuse or another app. Inspect leaves an unsubmitted
form open and returns the current declaration texts.

## 2. Create after explicit approval

Show all returned declaration texts and the selected developer account, name,
language, pricing, contact and intended package to the user. Only after they
approve those declarations, call `play_create_app` with the same body plus:

```json
{
  "requestId": "a-stable-unique-request-id",
  "declarations": ["exact reviewed text from inspection"],
  "confirmation": "CREATE com.example.game"
}
```

Keep `requestId` and all parameters unchanged on recovery. Changed/missing legal
texts block the operation. Never fabricate declarations or accept terms merely
because the user said "add purchases". Creation returns `consoleAppId` and may
return `packageEntered: true`; this only confirms that the field was filled.
`packageBound: "unverified"` means binding is not yet verified. Verify the actual
bundle package and Console result rather than assuming creation proves it.
The backend stores the project-to-Console binding for first upload. Creating via
the worker CLI alone does not create that panel binding; do not invent one.

## 3. Prepare the app and signed AAB locally

Explain the plan in plain language and resolve the game/package first. The agent
can perform these steps with authorized filesystem/terminal tools; they are not
MCP methods. If the chat cannot run local commands, report that limitation and
guide the operator rather than claiming a build was made.

- Use the actual game and its engine/build pipeline. Do not replace a Unity game
  with a Flutter shell merely to satisfy IAP prerequisites. A minimal Flutter app
  is appropriate only when explicitly requested for an isolated upload test.
  Do not upload a smoke app to an existing production package.
- For an approved new Flutter smoke project, choose a separate directory and use
  `flutter create --platforms=android --org <verified-org> --project-name <valid_name> <directory>`.
  The generated Android application ID is not necessarily the requested package:
  set and verify `applicationId`, and keep namespace/activity references coherent.
  Set the chosen app label and `version: <versionName>+<versionCode>` in pubspec.
- Integrate the compatible billing SDK, not just the permission. For Flutter,
  use `flutter pub add in_app_purchase`, inspect the resolved dependencies and
  include `com.android.vending.BILLING` in the resulting manifest. A smoke screen
  may check billing availability, but must not initiate purchases. Existing games
  should retain their purchase, reward and receipt-validation architecture.
- Inspect existing release signing before editing. Reuse the correct upload key
  for an existing Play app. For a new isolated test app, ask whether to create a
  separate upload-keystore and use Google Play App Signing. That approval does not
  authorize replacing another app's key, accepting new legal terms or publishing.
- Generate the approved key locally using the installed JDK's `keytool`; use a
  cryptographically random password and a local secret file/process environment,
  not a password literal in command history, chat or tool output. Configure the
  Android **release** signing configuration to use this key, not debug signing.
  The upload key and Google-managed app-signing key have different purposes.
- Before creating secrets, ensure the keystore and password properties file are
  ignored and untracked. Verify with `git check-ignore` and `git ls-files` without
  reading secret contents into context. Do not overwrite an existing key or put
  signing passwords in `assets/.env` (that file is for the Superleo connection).
  Tell the user to back up both the keystore and its password file securely.
- Run the game's checks and build. For the Flutter smoke app: `flutter analyze`,
  `flutter test`, `flutter build appbundle --release`. Find the actual output
  (normally `build/app/outputs/bundle/release/app-release.aab`); a local build is
  not proof of successful store upload or device billing.

Validate the output before requesting upload: use `bundletool validate` and
`bundletool dump manifest --bundle=<file> --module=base`, verify package,
versionCode and BILLING permission, and verify the release signature with
`jarsigner -verify`. Check the resolved billing dependency as well. Compute SHA-256
from the final signed file (PowerShell `Get-FileHash -Algorithm SHA256` or
`sha256sum`); normalize the hash to lowercase for the request. Do not change the
artifact after approval. For subsequent uploads use an unused versionCode; never
increment blindly to work around an uncertain upload result.

## 4. Stage and upload as an internal draft

The signed file must exist on the **browser host** in `PLAY_CONSOLE_AAB_DIR`.
If build and browser run on the same host, an operator can configure that directory
to the build output or a dedicated staging directory. For different hosts, use an
explicitly authorized file-transfer mechanism or ask the operator to copy it;
MCP does not transfer the AAB. Recheck the destination hash. Do not send arbitrary
paths/URLs as request fields or claim a local path is available remotely.
The browser host needs Java (`PLAY_CONSOLE_JAVA` if not on PATH) and a configured
`PLAY_CONSOLE_BUNDLETOOL_JAR`. Do not overwrite the host's other provision settings.
If the deployed worker lacks this flow, ask the operator to update/restart that
worker; an updated skill alone does not update the backend or browser service.

`play_upload_internal_draft` body contains `projectId`, `app`, `devId`,
`consoleAppId`, stable `requestId`, `artifactName` (filename ending `.aab`),
`sha256` (lowercase file hash), integer `versionCode`, `releaseName`, and
`confirmation: "UPLOAD <package> <versionCode> <sha256>"`. For a game without in-app purchases add
`billing: false`; otherwise the worker requires the BILLING permission in the AAB. Show these exact values
and the internal-testing destination for user approval before `confirm: true`.
For an existing app with an already uploaded draft, do not create/upload again.
Use `play_adopt_internal_draft` with `{projectId, app, devId, consoleAppId, requestId,
versionCode, releaseName, sha256, confirmation: "ADOPT INTERNAL <package> <versionCode> <sha256>"}`
and `confirm: true` after approval. The hash must come from the verified signed
build for this package. The worker downloads the Console original AAB and checks
that hash before writing a binding/receipt; no invented receipt or journal deletion.

The worker checks path confinement, hash, package, version and BILLING permission.
Permission alone does not prove SDK/runtime integration: still follow `iap.md`.
Signing enrollment and unknown UI states stop for manual handling. The worker
saves **only a draft** and refuses to replace an existing draft. It does not accept
additional signing terms, send for review, publish, or start a rollout.

The updated worker selects the Android create control despite duplicate button
labels, opens the file chooser through `Upload`, and waits for the processed
version in `Artifacts` (the filename is outside that grid). A file-transfer timeout
does not cause a second upload: the worker checks whether processing succeeded.
After `Save as draft`, it waits for the save acknowledgment, reopens internal
testing, and checks the release name and draft status. Treat `draftSaved: true`
as a saved draft only, never as a released or billing-ready app.

## 5. Select existing testers through MCP

Do not send the user to Console for a supported tester-selection operation.
Inspect with `play_inspect_internal_testers`, body `{projectId, app, devId,
consoleAppId}`, `confirm: true`. It returns list names, user counts and selection
state, not email addresses. Require these commands in the deployed catalog and
worker health; missing commands require deployment, not user Console actions.

After authorization, call `play_add_internal_testers` with the same identity,
stable `requestId`, `testerLists: [{name, users}, ...]` copied exactly from the
inspection, and `confirmation` constructed as
`"ADD INTERNAL TESTERS " + app + " " + JSON.stringify(testerLists)`.
Use `confirm: true`. It requires the project binding from creation/adoption.
An explicit request for all existing testers permits choosing all inspected
nonempty lists; do not add unknown/new lists or use a blind Select all control.
If the user requests a specific list, leave all other choices unchanged.

This operation adds selections only, preserves existing selections, saves and
reads them back. A changed count, duplicate name, incomplete/virtualized table,
unexpected unsaved changes or mismatched readback blocks the write/result.
Already selected lists are a live-verified no-op, including recovery from a lost
save response. Other uncertain writes are not automatically retried.
It does not create/edit lists, add email addresses, change Google Groups, or
publish. A list called "License testers" is not proof that the user's actual
device account has license-testing privileges; `licenseTestingVerified` stays
false. Do not claim purchases cannot charge money based on list selection alone.

## 6. Release and verify before IAP

`play_inspect_internal_release` body is `{projectId, app, devId, consoleAppId}`.
It returns visible state for review, not a definitive billing-ready boolean.
For release, require deployed catalog `play_release_internal` and worker health
`confirmedInternalRelease: true`. Complete signing/declarations and resolve all
Console errors/warnings first. Show the exact package, version, release name, hash
and **internal testing only** destination and obtain separate approval; "add IAP"
or approval of a draft upload is not release approval.

Call `play_release_internal` with `confirm: true` and body:
`{projectId, app, devId, consoleAppId, requestId, versionCode, releaseName, sha256,
confirmation: "RELEASE INTERNAL <package> <versionCode> <sha256>"}`.
The panel requires a verified draft receipt from `play_upload_internal_draft` or
`play_adopt_internal_draft`; the worker independently requires the matching
verified journal. Use adoption for older/external drafts, not another upload or
fabricated receipts. Request IDs and payloads remain stable on recovery.

The worker opens only internal testing, verifies the draft/version again on review,
and clicks `Save and publish` only when validation is clear. Unexpected confirmation
dialogs are not accepted. After submitting, failure to confirm persisted status
is an **unknown outcome**, not permission to try again. If the release warns that
no testers are specified, use the tester-list operations above, then re-inspect
before retrying; do not ignore the warning. Release itself does not configure
testers, license testing, consent/legal declarations or production releases.
`released: true` means internal release status readback, not verified purchases.

Then verify the actual version/track and billing configuration readiness before
continuing Google Play IAP creation. Never equate a saved draft with a live release.

On timeout or failure, retain IDs and inspect the current browser state and worker
journal (`out/onboarding-journal`). A persisted uncertain intent blocks repeats,
even with a different request ID. Only an operator may reconcile that journal after
checking Console; do not delete it to force a retry. A reused result is historical
verification, not a fresh live status check.

If a locator fails, stop and give the operator the worker screenshot/page-dump
location and failing step. Do not substitute ad hoc browser writes for panel/MCP
operations or broaden selectors blindly. Reconcile partial results before retrying.

## Evidence and handoff

Live test on 2026-10-07: app creation through the worker CLI succeeded; a separately
built Flutter billing AAB was uploaded and its internal draft saved/read back using
a diagnostic browser script. Those upload actions were then moved into the main
worker and covered by nine passing tests. This is not evidence of a complete
panel/MCP live run, automatic Flutter building, or working device purchases.
The internal-release path has fixture coverage and live pre-submit UI inspection;
the actual publish click and post-release selectors have not been tested live.
Do not reuse that test's package, app ID, key or journal for another game.

Report separately: local build/checks, artifact package/version/hash, upload/draft
readback, signing state, release state and IAP/device verification. Include local
artifact/key locations without passwords. Leave publication, missing host access
or unknown outcomes explicit. Do not auto-commit keys, generated builds or the
user's unrelated changes.
