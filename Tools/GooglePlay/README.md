Local Android release preparation only. Nothing here creates provider/store records,
installs a toolchain, generates signing keys, or uploads/publishes.

Run commands on Windows through `on-remote` from the synced project:

```text
on-remote powershell -NoProfile -ExecutionPolicy Bypass -File Tools/GooglePlay/preflight.ps1 -CheckDevices
```

The probe honors `UNITY_EXE`, discovers the exact project Unity version including
`C:\Unity\Editor\Unity.exe`, and checks bundled plus external EditorPrefs and
environment SDK/NDK/JDK candidates. Availability does not prove effective selection.
Device counts omit serials; a stopped adb server is left stopped.

After approval of the actual package ID, version/code and upload key, populate a
private process environment from existing signing credentials:
`CATMINERS_UPLOAD_KEYSTORE`, `CATMINERS_UPLOAD_ALIAS`,
`CATMINERS_UPLOAD_STORE_PASSWORD`, `CATMINERS_UPLOAD_KEY_PASSWORD`, and
`CATMINERS_UPLOAD_CERT_SHA256` (expected upload certificate SHA-256).
Never put passwords in arguments, tracked files or logs. The keystore path should
be absolute and outside the CI mirror. No default app ID is supplied.

`build-aab.ps1` requires `-Package`, `-Version`, `-VersionCode`, and a fresh absolute
`-Output` ending in `.aab`. It refuses another batch Unity job and always mirrors
into `build/google-play-ci` without running Setup. `UNITY_EXE` / `-UnityExe` is
optional. Do not start another Unity batch workflow concurrently.

`CatMiners.Editor.GooglePlay.BuildAndroid.Aab` requires the same public arguments as
`-catminersPackage`, `-catminersVersion`, `-catminersVersionCode`,
`-catminersOutput`. It builds only CoreGame, ARM64, IL2CPP, API 36, with no
development/debug/profiler options. Temporary settings are restored in `finally`.
`CatMiners.Editor.GooglePlay.BuildAndroid.Apk` is a signed release APK companion
requiring the same release inputs and signing. The existing
`CatMiners.Editor.BuildAndroid.Apk` embedded in `BalanceValidatorCli.cs` and
`Tools/unity.ps1 -Mode BuildApk` remain a separate legacy QA workflow, producing
debug-key APKs; they must not be used for Google Play release.
Direct entry points can use already configured Editor signing or the environment.
The AAB entry point and wrapper verify the signature and expected upload
certificate; direct AAB invocation also requires `CATMINERS_UPLOAD_CERT_SHA256`.
The companion APK needs a separate final APK signature check before distribution.

Release is deliberately blocked until Android icon slots and real integrations
are configured. Integration owners must implement
`CatMiners.Editor.GooglePlay.BuildAndroid.IReleaseGate`
for AppMetrica, Adjust, Meta, MAX, Billing and MulticastPayments. Its `Validate`
method must inspect actual installed/native SDK versions, app-specific production
settings, consent-aware bootstrap wiring and purchase server validation, returning
errors (empty only on success). Missing validators block the build; there is no
boolean manifest or command-line bypass. Do not implement placeholder validators.
Provider delivery and device testing remain separate evidence gates.

The wrapper verifies the final AAB signature and expected upload certificate and
rejects unsigned artifacts. A successful build receipt is **not upload readiness**.
Before any upload, verify the final merged manifest (identity/version/API and no
debuggable), resolved native Billing >=8, **every** engine/provider native library
ELF alignment and generated APK packaging on 16 KB pages, device behavior and
Play App Signing enrollment. Confirm the version code is unused in all Play tracks.
External writes/uploads/product creation require separate user confirmation.

Sources: [Unity build options](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/BuildPlayerOptions.html),
[Unity platform icons](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/PlayerSettings.GetPlatformIcons.html),
[target API](https://support.google.com/googleplay/android-developer/answer/11926878),
[Billing deadlines](https://developer.android.com/google/play/billing/deprecation-faq),
[16 KB checks](https://developer.android.com/guide/practices/page-sizes).
