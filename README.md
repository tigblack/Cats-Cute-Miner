# Cats Cute Miner

Play WebGL **0.1.18**: [Cat Miners](https://sites.aimulticast.org/sargsyan/cat-miners/?v=0.1.18). The final entrance uses native pixel-art pieces, a four-tile approach with destructible rocks, and four adjacent mining positions, one cat per work cell. After rescuing Bruno, the objective switches to clearing the mine and delivering remaining cargo. Hiring prices use the normal formula in every mine, including existing saves; the unintended 99% final-excavation discount is removed. Factory taps open the manager upgrade window; broken chains offer Activate and a full-screen chest reveal. Factory chests guarantee their own manager and cover automation copies through Mine 7, with diminishing help later. Mine 1 has two columns of blocks and a small right-hand debris branch (15 targets total); the HUD shows gold, elixir and gems. Native pixel-art scenery sits in recessed equipment bays with matching floors and shadows; locked toolbar buttons are dimmed with large centered golden locks replacing their icons; labels remain visible. Mia's Store greeting appears once for two seconds, with the receipt saved across reloads. An automatic completion window offers Stay here or Next mine; staying keeps the current mine and the green banner reopens the choice. All seven Base upgrades are open from Mine 1. The collapsed entrance, varied surroundings, Bruno and Equipment progression are included; browser saves stay on the same address.

Android **0.1.20 (10)**: [installable APK](Builds/Android/CatMiners-0.1.20-rewarded.apk), signed with the existing Android local install key for device checks. Includes rewarded features and Damage ×2 for five minutes. Reward buttons prepare and show an ad instead of opening the analytics screen; unknown age can explicitly confirm16+ without opting into analytics. All reward buttons use the gold Double Income style. Native artifact verification passed; actual phone ad delivery and AdMob published GDPR-message membership remain unverified. [Launch fix and validation](Docs/Rework/REWARDED_AD_LAUNCH.md), [damage bonus](Docs/Rework/REWARDED_DAMAGE_BONUS.md). This APK has not been published to Google Play.

Unity sources, gameplay data, project settings, development documents, release preparation, and marketing assets.

## Run the game on macOS

Open `Builds/macOS/Cat Miner.app` in Finder. This generated Apple Silicon app is available on the current Mac; build outputs are not committed.

To rebuild it in Unity **6000.6.4f1**, use **Cat Miners → Build → macOS App**. To play in the Editor, open `Assets/Game/Scenes/CoreGame.unity` and press **Play**.

For level cheats, use the Editor or `Builds/macOS/Cat Miner Dev.app` (**Cat Miners → Build → macOS Development App**). Open **DEV 4**: **+1 / +10** load the next mines immediately; enter a mine number **1–144** and press **Load** or **Enter** to jump directly. Level cheats automatically switch to the separate Debug profile. **Main** returns to your normal progress.

The recovered source archive still needs the two original launcher-icon PNG files from Git LFS. They do not block local gameplay. `python3 Tools/check-assets.py` lists remaining LFS pointers. See [launch repair and validation](Docs/LAUNCH_REPAIR.md) for the checks and remaining SDK configuration warnings.

## Open on macOS

1. Install Git and [Git LFS](https://git-lfs.com/). With an existing Homebrew installation: `brew install git-lfs`.
2. Run `git lfs install`, then clone this private repository using your authorized GitHub account:

   ```sh
   git clone https://github.com/tigblack/Cats-Cute-Miner.git
   cd Cats-Cute-Miner
   git lfs pull
   git lfs fsck
   ```

3. Install [Unity Hub](https://unity.com/download) and [Unity 6000.6.4f1](https://unity.com/releases/editor/whats-new/6000.6.4f1) (matching `ProjectSettings/ProjectVersion.txt`). Choose the Apple Silicon Editor for an Apple Silicon Mac, or the Intel Editor for an Intel Mac.
4. For Android development, install Android Build Support with Android SDK & NDK Tools and OpenJDK through Unity Hub. For the browser playable, also install WebGL Build Support.
5. Add the cloned folder in Unity Hub and open it. Git and internet access are required for the packages declared in `Packages/manifest.json`. Unity rebuilds its local Library cache on the first import. Open `Assets/Game/Scenes/CoreGame.unity` and press **Play**.

`Assets` includes all `.meta` files and bundled SDK binaries. Never open an LFS pointer file as if it were an actual image, font or DLL; run `git lfs pull` first. Unity files and source package versions are preserved; `Packages/packages-lock.json` is included.

## Project layout

- `Assets`, `Packages`, `ProjectSettings`: the Unity project.
- `Docs/TZ`: gameplay specification and revisions.
- `Docs/Rework`: implementation contracts and current game systems.
- `Docs/Marketing/ads`: completed marketing videos.
- `Docs/Legal/CatsCuteMiner`: game-specific legal drafts, not approved published policies.
- `google-play-publish-2026-10-17-complete-integrations`: the current publishing skill bundle and references.
- `release/google-play`: preparation state and curated evidence. Open statuses are not release-readiness claims.
- `Tools`: build, test, content and reporting helpers.

The local macOS app build is available from the Unity menu above. For Android on macOS, use `sh Tools/GooglePlay/preflight-macos.sh` and the guarded signed-AAB workflow in [Tools/GooglePlay/README.md](Tools/GooglePlay/README.md). Windows PowerShell wrappers remain available for Windows. Use the exact Unity version above before changing package versions.

The Android package and reserved iOS bundle ID are `com.multicastgames.catscuteminer`. The owner explicitly authorized internal and production publication on 18 October; current signing, legal documents, artifact and QA checks are still required. See [release status](release/google-play/status.md). Browser account sessions and management credentials are not contained in this repository. The existing upload key must be made available separately through private storage.

Generated builds, Library, Temp, logs, personal agent history, private service/guest credentials and signing keys are ignored. Backend QA guest sessions are not gameplay accounts and must not be copied into a client build.

## Base upgrades available immediately — 22 October 2026

All seven Base branches are available from the beginning of Mine 1, including on existing saves. Opening no longer waits for deliveries, purchases or later mines. Prices, upgrade ranks, local resets and the fixed HP of final excavations are preserved. [Implementation and checks](Docs/Rework/BASE_ALL_OPEN.md). This supersedes the earlier staged Base unlocks described below.

## Blacksmith and final excavations — 22 October 2026

Bruno is rescued near the end of Mine 2 and becomes the permanent blacksmith at the base. Equipment unlocks after both his rescue and actual Mine 2 completion; the starter pickaxe and real equip action are available before entering Mine 3. Existing equipment owners migrate once without replaying the rescue.

Every newly started Mine 3+ has one mandatory collapsed mine entrance with fixed shared HP, finite cargo, six rubble-clearing art states and normal offline mining. The latest visual revision replaces crystals/fossils with boulders, broken beams, rails and old machinery, and stages native pixel-art equipment in recessed bays with matching floors and shadows; see [COLLAPSED_MINE.md](Docs/Rework/COLLAPSED_MINE.md). The compact revision has four adjacent work slots and eight ordinary approach rocks; its declared Mine 3 reference measured 635.10 seconds. Earlier 23 timing measurements describe the previous wide formation. See [COMPACT_FINAL.md](Docs/Rework/COMPACT_FINAL.md) for current validation and saved-run limits. Hiring uses normal upgrade prices; the old 99% hiring subsidy was removed in 0.1.16. The historical HP benchmark is preserved separately, so a full crew is not guaranteed from ordinary cargo income. Ordinary target HP stays unchanged. Started saves retain their existing layout.

The current implementation, exact affordable reference profiles, 23 timing measurements, ordinary-HP comparison across 25 mines, Unity screenshots, offline caps and verification limits are in [FINAL_EXCAVATIONS.md](Docs/Rework/FINAL_EXCAVATIONS.md). The section below records the earlier first-session revision; Equipment timing and the short Cards/Equipment introductions are superseded by the new specification.

## First session — 21 October 2026

Mine 1 has a goal: rescue Mia, a cat locked in a cage at the far end of the mine. The HUD shows "Rescue Mia" with truthful route progress, and a short camera shot of the cage with Mia's line opens the mine. The cats clear the route and break the lock automatically; one "Mia is free!" window follows. This rescue is the only tutorial: since the owner's revision later the same day there are no paws, feature intro windows or hint bubbles.

Features unlock per mine as permanent profile milestones. Store, Cards, Base, Equipment and Sell Goods are always on the bottom bar. A locked feature has a small lock on its button; tapping it shows when it unlocks:
- Mine 1: Mining Power after the first delivery, then Hire Cat after the first Mining Power purchase.
- Mine 2: Cards and the Store (they unlock together), Carry Capacity, Sell Goods. Cards come with one starter card and resources for one upgrade.
- Mine 3: Equipment (with a starter pickaxe) and Movement Speed.
- Mine 4: Unloading Speed.
- Mine 5: Coin Value.
- Mine 6: Loading Bays, a new 7th branch that adds unloading spots used in parallel.

The rescued Mia runs the Store: her portrait and greeting appear at the top of the Store screen. She no longer works in the mines. Store keeps its icon on the left. Market is now **Sell Goods** with a crate icon. Repeatable branches gained milestones.

Settings live in `Assets/Game/Resources/Outpost/progression.json` (progression-2), `outpost_balance.json` (b8) and `stage_rewards.json` (rewards-2). The milestone table, tuning, measurements, tests, screenshots (`Docs/Reports/first-session/`) and remaining limitations are in [Docs/Rework/FIRST_SESSION.md](Docs/Rework/FIRST_SESSION.md).

Verification after that revision covered only the affected suites: the core first-session, localization, card and store tests (172 passed), and the Unity first-session, save-reload, HUD, Store and Sell Goods tests (all passing after one test fix). Mines 1–3 of a new profile were played in the Unity scene. The first session has not been checked on a phone. The video in `Docs/Reports/first-session/recording/` still shows the earlier version with the tutorial.

## Continuation checkpoint — 17 October 2026

The latest changes include mine obstacles and destructible debris, wall-mounted pipe visuals, Nunito UI fonts, rewarded bonuses, SDK telemetry, store artwork, legal drafts and advertising materials. The mine dressing implementation and planned validation are described in `Docs/Rework/MINE_DRESSING.md`.

Before this handoff, the focused `MineDressingTests` suite passed all 88 ordinary tests through dotnet (two explicit report generators were not selected). This includes placement and save/load checks across 25 sampled mines and automatic completion of seven sampled mines. The macOS launch repair, current PlayMode checks and remaining release limitations are recorded in [Docs/LAUNCH_REPAIR.md](Docs/LAUNCH_REPAIR.md).

The fast dotnet checks passed for Runtime, DevTools and Editor. The standalone PlayMode checker includes `CatMiners.Playable` as a project reference. Unity is still the authoritative check for compilation, asset import and PlayMode execution.

### Mine dressing rules `dressing-2` — 17 October 2026

Destructible obstacles now appear throughout each mine instead of a few small piles at chamber edges: barricades across passages (branch entrances first, then evenly along each passage, at least 6 cells apart), denser debris in every section, debris sprites as tall as rocks, and three new 2×1 barricades (boarded planks, box wall, sack wall). On the 25-mine sample, mines contain 17–98 debris piles (previously 13–69), and the measured isolated-mine completion time is ×1.14–×1.47 of the undressed mine (median ×1.27). A mine already started with `dressing-1` keeps its layout until completion; the new rules apply to the next mine or to a mine loaded through DEV 4. Details, the exact sample and the checks that were not completed are in `Docs/Rework/MINE_DRESSING.md`.

Rules `dressing-3` (same day, owner request): no empty floor remains in a mine. Every cell left free after obstacles and debris is filled with the mine's cheapest ordinary ore (gold only, no chest or elixir cycle); only the base, gate cells, the exit, factory pipe outlets and the two cells behind each gate stay open. Upgrade prices of a new mine are multiplied by the ratio of its total ore value to the undressed mine (×1.35–×2.24 on the sample). Mine length is estimated, not measured, at about ×1.4–×2.0; `OreFill.HpFactor` in `mine_dressing.json` makes the fill lighter if needed.

On a Mac, `Tools/unity-mac.sh` runs Unity tests in an isolated `build/ci` copy while the project stays open in the Editor.

### Playable ads — 17 October 2026

Three single-file HTML5 playable ads are built by `Tools/.venv/bin/python Tools/Playable/build_playable.py` into `release/playables/` (`cat-miners-short.html`, `-medium.html`, `-long.html`, about 0.33 MB each, no external requests, plus an `index.html` preview page). Short sends the player to the store on the third tap; medium is one complete mine with quests, a gate and chests; long runs mine after mine for up to 30 minutes. Every game screen has a PLAY NOW store bar at the bottom. They were checked with headless Chrome screenshots and autoplayer runs only — not yet on phones or in the ad networks' validators — and the store links still need confirming (the Google Play listing is a draft and there is no App Store link yet). Details: `Docs/Playable/PLAYABLE_ADS.md`.
