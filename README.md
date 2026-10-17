# Cats Cute Miner

Unity sources, gameplay data, project settings, development documents, release preparation, and marketing assets.

## Open on macOS

1. Install Git and [Git LFS](https://git-lfs.com/). With an existing Homebrew installation: `brew install git-lfs`.
2. Run `git lfs install`, then clone this private repository using your authorized GitHub account:

   ```sh
   git clone https://github.com/tigblack/Cats-Cute-Miner.git
   cd Cats-Cute-Miner
   git lfs pull
   git lfs fsck
   ```

3. Install [Unity Hub](https://unity.com/download) and [Unity 6000.3.11f1](https://unity.com/releases/editor/whats-new/6000.3.11f1). Choose the Apple Silicon Editor for an Apple Silicon Mac, or the Intel Editor for an Intel Mac.
4. For Android development, install Android Build Support with Android SDK & NDK Tools and OpenJDK through Unity Hub. For the browser playable, also install WebGL Build Support.
5. Add the cloned folder in Unity Hub and open it. Git and internet access are required for the packages declared in `Packages/manifest.json`. Unity rebuilds its local Library cache on the first import.

`Assets` includes all `.meta` files and bundled SDK binaries. Never open an LFS pointer file as if it were an actual image, font or DLL; run `git lfs pull` first. Unity files and source package versions are preserved; `Packages/packages-lock.json` is included.

## Project layout

- `Assets`, `Packages`, `ProjectSettings`: the Unity project.
- `Docs/TZ`: gameplay specification and revisions.
- `Docs/Rework`: implementation contracts and current game systems.
- `Docs/Marketing/ads`: completed marketing videos.
- `Docs/Legal/CatsCuteMiner`: game-specific legal drafts, not approved published policies.
- `google-play-publish-2026-10-16-emulator-consent`: the updated publishing skill bundle and references.
- `release/google-play`: preparation state and curated evidence. Open statuses are not release-readiness claims.
- `Tools`: build, test, content and reporting helpers.

Existing automation primarily uses Windows PowerShell, Windows Unity paths and Windows JDK executable names. Opening/editing the Unity project on a Mac does not port these scripts; macOS build/test wrappers and the signed-release verification entry point still need adaptation. Use the exact Unity version above before changing package versions.

The Android package and reserved iOS bundle ID are `com.multicastgames.catscuteminer`. Google Play upload/release remains subject to the previously deferred artifact approval. Browser account sessions and management credentials are not contained in this repository; log in to service dashboards separately on the new machine. Existing signed upload keys, if any, must be transferred separately through private storage.

Generated builds, Library, Temp, logs, personal agent history, private service/guest credentials and signing keys are ignored. Backend QA guest sessions are not gameplay accounts and must not be copied into a client build.

## Continuation checkpoint — 17 October 2026

The latest changes include mine obstacles and destructible debris, wall-mounted pipe visuals, Nunito UI fonts, rewarded bonuses, SDK telemetry, store artwork, legal drafts and advertising materials. The mine dressing implementation and planned validation are described in `Docs/Rework/MINE_DRESSING.md`.

Before this handoff, the focused `MineDressingTests` suite passed all 88 ordinary tests through dotnet (two explicit report generators were not selected). This includes placement and save/load checks across 25 sampled mines and automatic completion of seven sampled mines. Unity PlayMode visual evidence for the latest dressing and fonts still needs to be generated and reviewed; importing and running this revision on macOS has not been verified.

The fast dotnet checks passed for Runtime, DevTools and Editor. The standalone PlayMode check is blocked by a missing `CatMiners.Playable` reference in `Tools/UnityCheck/PlayModeTests/PlayModeTests.csproj.template`; the actual Unity PlayMode assembly definition already includes that reference. Run the tests in Unity or update that helper before relying on its PlayMode result.
