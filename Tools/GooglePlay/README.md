Local Android release preparation. These tools do not create provider/store records,
install a toolchain, or upload/publish. `new-upload-key.ps1` creates a key only when
no key/configuration exists; `inspect-signing.ps1` verifies an existing key without replacing it.

Run commands on Windows through `on-remote` from the synced project:

```text
on-remote powershell -NoProfile -ExecutionPolicy Bypass -File Tools/GooglePlay/preflight.ps1 -CheckDevices
```

The probe honors `UNITY_EXE`, discovers the exact project Unity version including
`C:\Unity\Editor\Unity.exe`, and checks bundled plus external EditorPrefs and
environment SDK/NDK/JDK candidates. Availability does not prove effective selection.
Device counts omit serials; a stopped adb server is left stopped.

Verify the existing key without exposing its passwords:

```text
on-remote powershell -NoProfile -ExecutionPolicy Bypass -File Tools/GooglePlay/inspect-signing.ps1
```

The private signing file stays in the Windows user profile under
`MulticastSigning/com.multicastgames.catscuteminer/`, outside the project and sync.
The public receipt is `release/google-play/evidence/upload-signing.json`.
`build-with-signing.ps1` loads it into the child process environment for the guarded builder.

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
`-catminersOutput`. It builds only CoreGame, ARMv7 + ARM64, IL2CPP, API 36, with no
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
External writes follow the current authorization in `release/google-play/profile.json`.
The user's explicit publication resumption on 2026-10-18 is recorded there; final
artifact, device, dashboard and production gates still apply.

Sources: [Unity build options](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/BuildPlayerOptions.html),
[Unity platform icons](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/PlayerSettings.GetPlatformIcons.html),
[target API](https://support.google.com/googleplay/android-developer/answer/11926878),
[Billing deadlines](https://developer.android.com/google/play/billing/deprecation-faq),
[16 KB checks](https://developer.android.com/guide/practices/page-sizes).

Current first release has no ads (owner decision 2026-10-19). MAX/rewarded and advertising consent remain deferred until the second release.

## macOS signed AAB counterpart

Run from the project root on the Mac (Python 3.9+, `/usr/bin/rsync`); prefix
commands with `on-remote` when working from the synced hub. No tool installation,
key creation, upload, Editor settings change or device/emulator launch is performed.

```sh
sh Tools/GooglePlay/preflight-macos.sh
```

Preflight emits JSON and exits 1 for blockers. It reads current Unity EditorPrefs
via `defaults export com.unity3d.UnityEditor5.x -`, honors embedded/external tool
selection, and lists environment candidates without selecting them automatically.
`--unity /absolute/path/to/Unity` (or `UNITY_EXE`) overrides discovery, but must
match `ProjectSettings/ProjectVersion.txt`. For this Mac the executable is
`/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity`;
`PlaybackEngines/AndroidPlayer` is **beside Unity.app**, under the version folder.
The selected SDK needs API 36, adb, build-tools binaries and their JARs. NDK needs
its host clang and CMake toolchain; JDK needs java/javac/jarsigner/keytool. NDK clang
and Java are probed without starting Unity. File availability does not establish
full Gradle/provider compatibility. Missing selected tools are reported, never
installed or silently replaced by environment candidates. Unknown preferences fail
closed. No adb command is run.

As of the read-only Mac check on 2026-10-18, Unity 6000.6.4f1 and selected bundled
SDK/API 36, NDK and JDK were available, with NDK/Java host probes successful.
On 2026-10-19 the owner adopted the game-specific13+ legal documents and confirmed
the actual support process. Public HTML pages are verified at
`https://sites.aimulticast.org/sargsyan/cats-cute-miner-legal/`.
The client now uses its exact `privacy/` and `terms/` pages. C# and Python gates
accept only the canonical game-specific pages in the correct roles; shared16+ pages,
other tenants, swapped documents and URL variants do not unlock SDKs. Age and
explicit-consent checks remain in place, and ads stay off. Native configuration is
applied before gates in the isolated release mirror for a normal build too.
The default publication workflow requires real adopted, live game-specific legal documents. An explicit owner request
for a local AAB can use `--local-artifact-only` on preflight/build: this skips only the
publication-URL prerequisite, preserves identity/signing/native/provider checks and
records `localArtifactOnly=true`, `legalConfigured=false`, `publicationReady=false`.
This fallback does not supply legal configuration or consent; if URLs are absent,
optional analytics remains inactive. No URLs or legal acceptance are invented, and the wrapper
does not upload or release anything. This mode applies native setup only in the isolated mirror.
Preflight checks legal configuration shape, not web availability or legal adoption.
The historical live Console signing readback is recorded in
`release/google-play/evidence/google-play-signing-current-20261018.json`.

Use the verified signing receipt referenced by `release.signing_evidence_ref` in
`release/google-play/profile.json`; its fingerprint must match the private keystore.
An upload key already bound in Play must be reused. For a new app whose Console
explicitly has no first-upload certificate, the separate guarded first-key procedure
is described in [FIRST_UPLOAD_KEY_MACOS.md](FIRST_UPLOAD_KEY_MACOS.md).
The builder never creates keys. Original Windows signing evidence is preserved.
Supply the five `CATMINERS_UPLOAD_*` environment
variables described above through a private parent process, or explicitly pass an
existing private JSON file outside the project with mode 600 (or stricter):

```json
{
  "package": "com.multicastgames.catscuteminer",
  "keystore": "/absolute/private/location/upload.jks",
  "alias": "upload",
  "storePassword": "<existing private value>",
  "keyPassword": "<existing private value>",
  "certSha256": "<SHA-256 from the active verified signing receipt>"
}
```

This is a schema example, not a file to save in the project. The explicit filename
must end in `.secret.json`; a privately transferred Windows file needs its keystore
path updated privately to the existing Mac keystore. JSON is parsed programmatically
and passed only through the child environment; it is never sourced or printed.
Passwords never appear in arguments. No secret file is auto-discovered. Preflight
never reads signing secrets. Build verifies the existing private-key entry and
certificate with keytool before launching Unity; Unity validates signing during
its guarded release build.

Once legal/signing configuration and release inputs are actually available, run
this exact command using the inherited private environment. Set the three public
shell variables first to the approved version, unused code and fresh absolute AAB
path (no default version/code is assumed):

```sh
sh Tools/GooglePlay/build-aab-macos.sh \
  --package com.multicastgames.catscuteminer \
  --version "${RELEASE_VERSION:?set approved MAJOR.MINOR.PATCH}" \
  --version-code "${RELEASE_VERSION_CODE:?set unused Play code}" \
  --output "${RELEASE_AAB:?set fresh absolute .aab path}"
```

Alternatively append `--signing-json "$HOME/MulticastSigning/com.multicastgames.catscuteminer/signing.secret.json"`
only after placing the existing private file there. On the hub, prefix the same
command with `on-remote`; variables must be populated in the process that invokes
it. On the Mac start in `/Users/arthuri/Unity Projects/Cats-Cute-Miner-main`.

The owner enabled `release/google-play/artifact-retention.json` on 2026-10-20:
the release builds folder keeps one verified game AAB. The macOS wrapper now runs
the actual manifest/Billing/ABI/16 KB ELF inspector after signature verification.
Only after the new artifact, builder receipt and inspection agree on its hash,
package and version code does it delete older `cat-miners-<version>-<code>-noads*.aab`
files and partial copies in `release/google-play/builds`. Failed builds preserve
older artifacts. Same/newer-code files and symlinks stop cleanup. Signing material,
unrelated filenames and evidence are not cleanup targets. Detailed native reports
go to `release/google-play/evidence/build-history/`. A local build does not verify
that the selected code is still unused in Console; confirm again before upload.

`build-aab-macos.sh` calls the shared standard-library helper
`google_play_macos.py`. It fixes the accepted package, requires a fresh absolute
output plus fresh `.build.json`/`.unity.log` siblings, authenticates the existing
certificate against the active verified signing receipt, and takes an exclusive persistent file
lock at `build/.google-play-macos.lock`. Never delete this lock file while a wrapper
is running. It refuses concurrent batch Unity jobs, any Editor on its mirror, and
an existing mirror `Temp/UnityLockfile` (including stale locks pending manual
inspection). Do not run any other Unity batch workflow concurrently.

Only `Assets`, `Packages` and `ProjectSettings` are mirrored into
`build/google-play-ci-macos`. Source settings and the user's open Editor are
untouched; Setup is never run. Symlinks and local package dependencies outside the
mirror are rejected. The shared Unity builder uses Unity's AndroidPlayer directory
API and chooses native JDK executable names for Windows or macOS. No C# source
rewrites are performed by the wrapper. All existing
`BuildAndroid.Aab` guards, legal/provider checks, release settings, CoreGame scene,
ARMv7+ARM64/IL2CPP/API 36, signature validation and settings restoration remain
active. The wrapper never edits `Assets/Game/Scripts/Editor/BuildAndroid.cs`.

After Unity exits successfully, the wrapper independently checks the receipt's
identity/version/path/Unity/scene/release settings/signature claims, actual AAB hash,
`jarsigner -verify` (including rejection of unsigned entries), and the single
`keytool -printcert -jarfile` certificate against approved evidence. Any failed
build or verification removes the AAB and receipt and preserves the private
`.unity.log`; retry with a new output/log path. Keep logs private and inspect them
locally rather than publishing their contents. The persistent mirror/cache is
retained for diagnosis; missing/stale locks are never cleared automatically.

Success means a locally signed artifact awaiting verification. It does **not**
verify native ABI contents, Billing resolution, ELF/APK 16 KB alignment, merged
manifest, device/provider behavior, production device coverage, Play App Signing
or unused Console version code. These remain separate release gates.
