# Ad footage capture from Unity: feasibility and harness plan (read-only audit)

## 0. Recommendation

**Use real-time lockstep at 30 fps.** It needs no change to the runtime code. Each shot runs in real time, paced to exactly 33.3 ms per frame by a busy-wait loop.
- The simulation is driven by `ManualClock`, set to an absolute time each frame. This part is fully deterministic.
- `Time.captureDeltaTime = 1/30` makes the scaled-time visuals deterministic.
- Everything that reads `Time.unscaled*` sees about 33 ms ± jitter per frame.
- Live audio can be recorded in sync, because game time and wall time match.

**Capture method:**
- Hook `RenderPipelineManager.endContextRendering` and issue `AsyncGPUReadback` (RGBA32) there.
- A background thread appends frames to one raw `.rgba` file per shot under `build/adcapture/`. This folder does not sync to the hub.
- A JSON sidecar goes to `Docs/Reports/ads/` and syncs to the hub on its own.
- A WAV is recorded live through the existing `GameplayRecordingTests.AudioTap`.

**Encoding and transfer:**
- Encode on the PC with ffmpeg from a venv inside the project: `libx264 -crf 12 -pix_fmt yuv444p -vf vflip`.
- Fetch the `.mp4` and `.wav` to the hub with `on-remote --fetch`, then composite there.

**Fallback if too many frames run over budget:** a small runtime change (a "presentation clock" that respects `captureDeltaTime`) makes capture fully deterministic at any speed. The list of places to change is in §2.4.

---

## 1. How tests run (facts)

### `Tools/unity.ps1`
- **Modes:** `Compile|EditMode|PlayMode|Validate|Setup|BuildApk|BuildWeb` (unity.ps1:25). **Targets:** `Auto|Real|Ci` (:27). Other parameters: `-TestFilter` (:30), `-BuildTarget` (:31), `-NoSync` (:33).
- **Ci target:**
  - Runs on `<root>\build\ci` (:266).
  - `robocopy /MIR` copies only `Assets`, `Packages` and `ProjectSettings` (:73-82). Ci has its own `Library`.
  - For PlayMode, a separate Unity `Setup` run happens first (:268-271).
- **Real target:** refused while the Editor holds `Temp\UnityLockfile` (:61-71, :234-236). `Auto` picks Ci when the project is locked (:231-233).
- **PlayMode arguments:** `-batchmode -projectPath … -logFile … -catminersReports "<root>\Docs\Reports" -runTests -testPlatform PlayMode -testResults … [-testFilter …]` (:141, :150-155).
  - **No `-nographics` for PlayMode or Setup.** Only Compile, EditMode and Validate add it (:143, :147, :156).
  - The PlayMode log confirms a real GPU device: `Direct3D 11.0 … Renderer: AMD Radeon(TM) Graphics` (Docs/Reports/outpost/unity-playmode-OutpostEnglishUiEvidenceTests.log:113-116). This is the Radeon iGPU, not the RTX 3060 Laptop GPU.
  - `-catminersReports` always points at the root `Docs\Reports`, even for Ci. `PlayModeUtil.ReportsDir()` reads it (PlayModeUtil.cs:20-26).
- **Results and logs:**
  - `Docs/Reports/unity-playmode.log` and `Docs/Reports/playmode-results.xml`.
  - Archived copies: `Docs/Reports/outpost/playmode-<filter>-results.xml` and `unity-playmode-<filter>.log` (:191-198).
  - The exit code is non-zero if any test failed or `total<=0` (:199). Failures are printed (:200-205).
- **Logged run times:**

  | Run | Time | Source |
  |---|---|---|
  | Setup (Ci) | 24 s | Docs/Reports/playmode-run.txt:2 |
  | PlayMode, 26 tests | 373 s | playmode-run.txt:5 |
  | First Ci compile | 174 s | ci-first-compile.txt:2 |
  | Full PlayMode, 105 tests | 867 s test time | outpost/playmode-all-results.xml |
  | Single `[Explicit]` evidence tests | 6–18 s each | outpost/playmode-OutpostStrikeEvidenceTests-results.xml: 5.97 s; …MineChestScreens…: 18.2 s |
  | Asset refresh per launch | 6.6–19.4 s | unity-playmode-*.log:328 |

  Expect about 2–4 minutes of overhead per invocation, plus the shots themselves in real time.

### PC environment (probed)
- Project root: `C:\Unity Projects\Cats Cute Miner`. Unity: `C:\Unity\Editor\Unity.exe` (third candidate, unity.ps1:47-51).
- No ffmpeg on PATH. Python 3.14.3. CPU Ryzen 5 6600H, 15.2 GB RAM, 184 GB free on C:.
- **Not a git repository** (`git rev-parse` fails) and **no `.gitignore`**, so nothing can be committed by accident. The only git checkout is `Builds\WebRepository` (Tools/Web/Publish-GitHubPages.ps1:9, 43, 58), which copies only `Builds\Web`.
- Earlier recordings exist: `build/recording/gameplay.avi` (95.6 MB) and `Docs/Reports/outpost/recording/outpost-gameplay.avi` (131 MB, plus a 5.3 MB `.mp4`).

### Mutagen
`/usr/local/share/claude-hub/mutagen-ignores.txt` ignores:
- `build` anywhere, `/Builds`, `.venv`, `venv`
- `*.png *.jpg *.mp4 *.wav *.ogg *.exe`

`*.rgba`, `*.raw` and `*.json` are **not** ignored. **Raw frames must never be written outside `build/`**, or about 250 MB/s would be synced to the hub.

### PlayMode test assembly
`Assets/Game/Tests/PlayMode/CatMiners.Tests.PlayMode.asmdef`:
- `includePlatforms: []` (any platform), `defineConstraints: ["UNITY_INCLUDE_TESTS"]`.
- References Core, Runtime, DevTools, `UnityEditor.TestRunner`, InputSystem, UGUI and TMP. Precompiled references: nunit and Newtonsoft.

Unity compiles any-platform assemblies for the Editor with `UNITY_EDITOR` defined and against `UnityEditor.dll`, so `#if UNITY_EDITOR` blocks using `UnityEditor.Media` compile. There is no precedent in this repo; a `-Mode Compile` run confirms it.

The URP runtime assembly is **not** referenced. Reach URP asset properties by reflection.

### Existing building blocks
- **Fixture pattern:** `OutpostCardsFixture` (Outpost/OutpostCardsUiTests.cs:20-61).
  - `ManualClock(1_800_000_000_000L)` assigned to `OutpostController.ClockOverride`.
  - Temp `SaveDirectoryOverride`, `CameraRig.OverrideAspect = 9/16`, then `LoadSceneAsync("CoreGame")`.
  - TearDown resets the statics.
- **`Capture()`** (Outpost/OutpostEnglishUiEvidenceTests.cs:53-103):
  - RT 1080×H with depth 24 and `FilterMode.Point`.
  - `scaler.enabled=false`, `canvas.scaleFactor=√(H/1920)`.
  - `ScreenSpaceCamera`, `worldCamera=cam`, `planeDistance=1`.
  - Waits 0.35 s of real time, then `RefreshNow`, `ForceUpdateCanvases`, `camera.Render()`, `ReadPixels(RGB24)`, `EncodeToPNG`, and restores everything.
- **Real-time video recorders:**
  - `GameplayRecordingTests` (legacy mode): `AudioTap` via `OnAudioFilterRead` (:268-297), MJPEG+PCM `AviWriter` (:303-512), output to `build/recording` (:186). The comment at :84-85 says the RT is read after Update and that **`WaitForEndOfFrame` never fires in batchmode**.
  - `OutpostGameplayRecordingTests`: Outpost mode with `SystemClock` (:34), `[Timeout(900000)]` (:81), and a `Grab()` that reads the RT the engine already rendered, without calling `Render()` (:105-122).
- **Prior evidence that captureDeltaTime does not help unscaled time:** comment at Outpost/OutpostStrikeEvidenceTests.cs:168-169: *"Time.captureDeltaTime does not fix unscaledDeltaTime"*. The workaround there is `Game.enabled=false` plus a manual `Game.Board.Sync(Game.Sim, 1f/60f)` (:174-189).
- **Busy-wait pacing:** `PerformanceTests.Pace()` (PlayMode/PerformanceTests.cs:328-340), with the rationale at :39-47 (Editor targetFrameRate waiting spins editor loops). It sets `targetFrameRate=-1` (:248).
- **No dev panel in tests:** the DEV panel is not auto-installed for test-loaded scenes, because `RuntimeInitializeOnLoadMethod` runs once per domain (Docs/Rework/map/tests-tools.md:431; OutpostDevPanel.cs:20-27).
- **New test files need a `.meta`:** `python3 Tools/gen_meta.py` (tests-tools.md:425; gen_meta.py:1-8).

---

## 2. Q1: deterministic 30 fps?

### 2.1 Clock and frame order

**Simulation.**
- `OutpostController.Update` (Loop.cs:27-48) calls `AdvanceOnline()`, which uses only `Clock.MonotonicSeconds` and `Clock.UtcNowMs` (Loop.cs:65-87).
- `Sim.Advance(baseTick + elapsedµs × speed)` works on absolute elapsed time, so there is no drift.
- Gap rule: a delta over 1 s, or a UTC/mono mismatch over 1 s, triggers `BeginOffline` (:71-75). Steps must stay ≤1 s.
- Autosave every 2 s of *clock* time (OutpostController.cs:22; Loop.cs:36-40).
- With `ManualClock` (Core/Sim/Clock.cs:21-41, public setters :23-24) the simulation is **fully deterministic**.
- `ManualClock.Advance(1/30)` adds `round(33.33)=33 ms` to UTC each frame (Clock.cs:38), so UTC drifts 10 ms/s against mono. **Set both readings absolutely from an accumulated time instead** (see `Advance()` in §6).

**Unity frame order in batchmode:**
1. `Update()`: controller simulation, `PresentEvents`, `Board.Sync(Sim, Time.unscaledDeltaTime)` (Loop.cs:45); HUD `Update` (OutpostHud.cs:884-900).
2. Coroutines resumed by `yield return null`, including the test.
3. `LateUpdate`: `CameraRig.LateUpdate` (CameraRig.cs:384-424; `Apply()` with the pixel snap at :426-438); `TutorialHint.LateUpdate` (TutorialHint.cs:380).
4. Canvas rebuild and URP render into `cam.targetTexture`.
5. (`WaitForEndOfFrame`: never fires in batchmode.)

**Is `yield return null` + `camera.Render()` + `ReadPixels` up to date?** Partly:
- It renders this frame's simulation and view state.
- But the camera transform is from the previous `LateUpdate`, so it lags one frame during smooth focus, kick or fling.
- UI layout is stale unless `Canvas.ForceUpdateCanvases()` is called, as `Capture()` does at :71-73. The tutorial hint position is stale too.
- The camera renders twice per frame (the engine renders again at end of frame), which wastes the frame budget.

Reading the RT right after `yield return null` *without* `Render()` gives the previous frame, fully consistent. This is proven by OutpostGameplayRecordingTests.cs:105-121. Every image is uniformly one frame late.

**Best option:** request the readback in `RenderPipelineManager.endContextRendering`. It fires inside step 4, after `LateUpdate`, so it captures the exact current frame with no second render. It does not depend on `WaitForEndOfFrame`.

### 2.2 With `captureDeltaTime=1/30` and ManualClock

**Deterministic:**
- Core simulation (ManualClock).
- `Time.time` / `Time.deltaTime` consumers:
  - Board target state `time` (OutpostBoardView.cs:382)
  - Lanterns, gears, glints (OutpostBoardView.Floor.cs:347-385)
  - BaseCamp lanterns (BaseCampView.cs:143, 286)
  - Effects sprites (Effects.cs:377-385)
  - Chip `ParticleSystem` (Effects.cs:95-126, scaled time)
  - `FloatingText` (FloatingText.cs:56)
- Seed `UnityEngine.Random` per shot for repeatable variants.

**Still on real time:**

| System | Code | Visible effect if frames are not paced |
|---|---|---|
| All board view animation dt | Loop.cs:45 → OutpostBoardView.Sync(dt) :391-418; cats `OutpostCatView.Apply(pose, dt)` :291, :320-352; strike FX `PlayerStrikeFx.Tick` (via TickStrikes :418) | Cat walk, mine and catch-up animations speed up or slow down against simulation motion |
| Camera smooth, kick, fling | CameraRig.cs:389, 397, 414-421 | Pans and shakes run on wall time |
| HUD coin fly, refresh throttle (0.1 s), toasts, flow stats | OutpostHud.cs:579, 638, 886-914 | Coin flights and counter update rate |
| Card chest toasts | OutpostHud.Cards.cs:325, 340 | Timing of card toasts |
| Window pop animation | UI/PixelPop.cs:36 | Pop speed |
| Tutorial and hint | OutpostTutorial.cs:220, 233, 336; TutorialHint.cs:306-383 | Avoid by skipping the tutorial |
| Transition fade | OutpostController.Commands.cs:144, 154 | Fade length |
| Audio throttles and scheduling | OutpostAudio.cs:40-64; Sfx.cs:205, 213, 236, 263; Ambience.cs:49, 74-75; MusicPlayer.cs:99 | Which SFX actually play |
| Audio DSP playback | engine | Always real time |
| Offline / time-skip catch-up slicing | Stopwatch, 8 ms per frame (Loop.cs:144-167) | Number of progress frames; results are unchanged |

Conclusion: **N consecutive frames at true 30 fps are only consistent if wall time is also paced at 33.3 ms per frame (lockstep) or the runtime is patched.** Under slow capture without pacing, cats animate faster than they move.

### 2.3 Three strategies

**A. Lockstep (recommended, no runtime change).**
- Set `captureDeltaTime=1/30`, `targetFrameRate=-1`, `vSyncCount=0` (GameBootstrap.cs:31-32 forces 60 and 0 on load).
- Busy-wait to frame boundaries with `Stopwatch` (PerformanceTests.cs:328-340).
- Set the clock absolutely each frame.
- Log `Time.unscaledDeltaTime` per frame. Re-shoot if any frame deviates by more than about 3 ms.
- Needs main-thread work under ~25 ms per frame (see §3).
- Live audio is then in sync (`AudioTap`). Prior real-time A/V capture worked: `recording.json` reports audio peak 0.53.

**B. Runtime patch (fallback, cleanest).**
- Add a static `FrameClock` in Runtime: `Delta => Time.captureDeltaTime > 0 ? <capture-time delta> : Time.unscaledDeltaTime`, plus `Now` accumulated by its own counter. Do not mix `Time.time` and `unscaledTime` thresholds when switching modes.
- Replace about 35 call sites: Loop.cs:45; Commands.cs:144, 154; CameraRig.cs:389, 397, 414, 415, 421; OutpostHud.cs:579, 638, 886, 891, 896, 899, 905, 914; OutpostHud.Cards.cs:325, 340; OutpostTutorial.cs:220, 233, 336; TutorialHint.cs:306, 313, 348, 383; PixelPop.cs:36; OutpostAudio.cs:40; Sfx.cs:205, 213, 236, 263; Ambience.cs:49, 74, 75; MusicPlayer.cs:99.
- Production behaviour is unchanged (`captureDeltaTime==0`).
- Capture can then be arbitrarily slow (PNG, supersampling). Audio must be rebuilt from the sidecar events.

**C. No-patch hybrid.**
- `Game.enabled=false`, then each frame call the private `AdvanceOnline(true)` and `PresentEvents()` by reflection, plus the public `Board.Sync(Sim, 1/30f)` and `Rig.SetContextTop(...)`. This mirrors Loop.cs:27-48, with precedent at OutpostStrikeEvidenceTests.cs:174-189.
- The board becomes deterministic. HUD, camera kick and smoothing, and audio stay on real time.
- Fragile (private names). Use only for HUD-less board close-ups.

**Cheap probe:** the sidecar `frameLog.dt` shows directly whether `unscaledDeltaTime` follows `captureDeltaTime` in 6000.3.11f1. The project comment says it does not.

---

## 3. Q2: per-frame cost at 1080×1920

Measured numbers from the project:
- Whole frame with a 1080×1920 RT and ScreenSpaceCamera HUD, unpaced, in Editor batchmode (legacy board): median **2.2–2.4 ms**, p95 ~3 ms (Docs/Reports/perf.json:54-58).
- Outpost paced at 60 fps: median 16.65 ms, p95 17.1 ms, **max 53.9 ms** (outpost/performance-editor.json:66-74).
- Durable autosave (fsync): **median 25.5 ms, p95 29.9 ms** (performance-editor.json:325-331). `RealFileSystem.WriteAllBytesDurable` calls `Flush(true)` (Core/Save/SaveService.cs:43-51).

Estimates below are **(est.)**. There are no measurements in the repo, so the harness should log its own Stopwatch timings.

| Step | Main-thread cost |
|---|---|
| Extra manual `camera.Render()` | ~1–3 ms (est.), redundant |
| `ReadPixels` 1080×1920, synchronous (GPU stall) | ~4–12 ms (est.) |
| `Texture2D.Apply()` after ReadPixels | ~2–5 ms (est.), **not needed** for CPU reads |
| `EncodeToPNG` 1080×1920 | ~80–250 ms (est.), not usable in lockstep |
| `EncodeToJPG` | ~20–50 ms (est.) at full resolution. The 540×960 JPG(80) path held 15 fps (GameplayRecordingTests.cs:100) |
| `GetRawTextureData<byte>()` (no copy) + write 6.2 MB to page cache | ~2–6 ms (est.) |
| **`AsyncGPUReadback.Request`** | ~0.1 ms; callback copy of 8.3 MB ~1–2 ms; latency 1–3 frames; file write on a background thread |

**Data rate:**
- RGBA32: 8,294,400 B/frame → 249 MB/s → **11.2 GB per 45 s**.
- RGB24: 6,220,800 B/frame → 187 MB/s → 8.4 GB per 45 s.
- 184 GB is free on the PC. Encode and delete each shot's raw file right away.

**Recommended:** `AsyncGPUReadback` (RGBA32) issued from `endContextRendering`, pooled `byte[]`, a background `FileStream` that writes at offset `idx*FrameBytes` (robust to out-of-order completion), one `.rgba` file per shot.
- Use `FileSystemOverride` with a non-fsync `WriteAllBytesDurable` so the 2-second autosave does not break the 33 ms budget. The seam is read in `Init` (OutpostController.cs:21, 114).
- Fallback: synchronous `ReadPixels` into RGBA32 without `Apply`, then `GetRawTextureData<byte>().CopyTo(pooled)`.
- Assert `SystemInfo.supportsAsyncGPUReadback`.

**Quality issue found:** the Editor batchmode run recorded `"qualityLevel": 0` (performance-editor.json:31). Level 0 is "Mobile" (QualitySettings.asset:10, 50), which uses `Mobile_RPAsset` with **`m_RenderScale: 0.8`** (Mobile_RPAsset.asset:29) and `m_UpscalingFilter: 0` (Auto, :30). That means internal 864×1536 with bilinear upscaling, i.e. soft pixel art.
- PC level 1 uses `PC_RPAsset` with renderScale 1 (PC_RPAsset.asset:29), but its renderer adds SSAO (PC_Renderer.asset:61-80); Mobile has none (Mobile_Renderer.asset:29).
- **Keep Mobile (the shipping look) and force `renderScale=1` by reflection**, restoring it afterwards. Log the quality name and old scale in the sidecar.
- Other settings:
  - Post-processing is off: the scene camera has no `UniversalAdditionalCameraData` (CoreGame.unity:300-370).
  - MSAA is 1x and HDR is on (RP assets :26-28).
  - Color space is Linear (ProjectSettings.asset:50). The RT bytes are sRGB-encoded, matching existing PNGs.
  - There is no Pixel Perfect Camera. The rig snaps to a 1/32-cell grid (CameraRig.cs:432-437; PixelArt.cs:40 `CellPixels = 32`).

---

## 4. Q3: encoding and transport

**(a) `UnityEditor.Media.MediaEncoder`.**
- Compiles under `#if UNITY_EDITOR` in this asmdef (see §1).
- Windows Media Foundation H.264 has **4:2:0 only**, bitrate-mode quality control, and no CRF, lossless or 4:4:4.
- `AddFrame(Texture2D)` needs a synchronous CPU texture (ReadPixels on the main thread).
- Untested in batchmode. Also verify the exact 6000.3 type names (`VideoTrackAttributes` / `VideoTrackEncoderAttributes`, `H264EncoderAttributes`).
- Verdict: preview only. It adds generation loss for pixel art.

**(b) Raw frames + ffmpeg on the PC (recommended).**
- No `.gitignore` or repo to worry about (§1).
- Venv under an ignored path: `build\adtools\venv`. Both `build` and `venv` are Mutagen-ignored, so it does not collide with the hub's Linux `Tools/.venv`. Install `pip install imageio-ffmpeg`; it is a py3-none-win_amd64 wheel that bundles ffmpeg.exe and works on Python 3.14.
- Per-shot encode (assuming the vflip holds; verify against `_last.png`):
  `ffmpeg -f rawvideo -pix_fmt rgba -s 1080x1920 -r 30 -i build\adcapture\<shot>.rgba -vf vflip -c:v libx264 -preset slow -crf 12 -pix_fmt yuv444p -g 30 -movflags +faststart build\adcapture\<shot>.mp4`
- For shots that need heavy zoom or crop on the hub: `-c:v libx264rgb -crf 0` (lossless, larger).
- Size estimate: about 10–40 MB per 10-second shot at CRF 12 4:4:4 (est.). The earlier 540×960, 15 fps, 75 s MP4 was 5.3 MB.

**(c) Transfer.**
- `on-remote --fetch <paths…>` uses sftp over the reverse tunnel (/usr/local/bin/on-remote:483-500). No size cap, but it **does not overwrite an existing hub copy**; delete it first (:485-489).
- Fetch `build/adcapture/<shot>.mp4`, `.wav` and `_last.png`. On the hub, `build/` is also sync-ignored, so the media stays on the hub.
- JSON sidecars in `Docs/Reports/ads/` sync automatically.
- Hub ffmpeg 7.0.2 has `libx264` (yuv444p supported), `libx264rgb`, `aac`, `ffv1` and `png`. Hub: 16 cores, 61 GB RAM.
- Final Meta encode: `-c:v libx264 -preset slow -crf 16 -pix_fmt yuv420p -profile:v high -r 30 -c:a aac -b:a 192k -ar 48000 -movflags +faststart`, 1080×1920.

---

## 5. Q4: HUD, tutorial, camera

**HUD.**
- Created as `ScreenSpaceOverlay`, `sortingOrder 30000`, `CanvasScaler` 1080×1920 with match 0.5 (OutpostHud.cs:254-284). Overlay is not rendered into the camera RT.
- Switch it as `Capture()` does. Keep it in ScreenSpaceCamera even for clean shots: the HUD insets feed `Rig.SetUiInsets` and the camera framing (OutpostHud.cs:715-745; CameraRig.cs:110-116, 211-228), and overlay mode at the 640×480 batch screen would change the framing.
- **Clean shot:** put a `CanvasGroup` on the HUD root (`Game.Hud.gameObject`) with `alpha=0`. Layout and insets stay the same.
- **Partial hide:** a `CanvasGroup` with alpha 0 on `SafeArea/Settings` (:407) and `SafeArea/TutorialSkip` (:415). The HUD may toggle `SetActive`, which is why alpha is safer.
- Other useful paths: `SafeArea/TopBar` (wallet), `ZonePlaque/sector` (:355-360), bottom `Base` button (:486), base rows `branch{(int)UpgradeBranch}/buy` (OutpostHud.BasePanel.cs:106, 128; enum OutpostIds.cs:49-57).
- **Debug-profile tag:** the Debug profile appends `"<color=#FF9070>[DEBUG]</color>"` to the zone plaque (OutpostHud.cs:60, 1018). Either:
  - strip it from TMP `sector` every frame (it is only rewritten when zone or gates change, :1009), or
  - copy `<save>/CatOutpostV2/debug/state.json` over `main/state.json`, delete `main/state.json.bak` and `.tmp` (OutpostSaveService.cs:77-78, 109-114), then `SwitchProfile(false)` (OutpostController.cs:227-239). Verify that main loads it.

**Tutorial and windows.**
- `Game.Tutorial.Skip()` (OutpostTutorial.cs:148-159, also hides the hint).
- `Game.Hud.CloseAllWindows()` (OutpostHud.cs:828).
- `Game.ClearSelection()` (Commands.cs:186): avoids the context panel moving the camera (Loop.cs:46; CameraRig.cs:124-171).
- `Hud.Toast` (:564) only when wanted.

**Camera** (`Game.Rig`, CameraRig.cs):
- `FocusOn(Vector2 x,z)` is instant and clamped to `MinX..MaxX`, `MinZ..MaxZ` (:327-336, 41-52). Drive it **every frame from the test** with your own easing; this is deterministic.
- `FocusOnSmooth(Vector2, s)` (:344-361) is eased on unscaled time.
- `ScrollWorld` (:317), `Fling` (:301, decays on unscaled time), `Kick(px)` (:369-374, 0.3 s shake).
- Topology maps use `FreeExploration` (X pan allowed; OutpostController.cs:168-169; 10 visible columns, :39, 216).
- The game moves the camera itself on `SectorOpened` (Loop.cs:111-115). A per-frame `FocusOn` overrides that, because it cancels smoothing (:331).
- World anchors: `Board.BaseFocusZ` (OutpostBoardView.cs:94), `Board.GateWorldPosition(order)` (:163), `Game.TryGetTargetWorldPosition` / `TryGetWorkerWorldPosition` (Commands.cs:167-176), `Board.CellCenter` (OutpostBoardView.cs:109).

**Zoom.** There is no zoom API. `Cam.orthographicSize` is set only in `FitWidth`, which runs only from `SetBoard` or `Refit` (:96, :189, :223; triggers at :115, :394). A test override persists; re-apply it each frame and call `FocusOn` to re-run `Apply()`.
- Pixel-perfect sizes: `size = 1920 / (2·32·k) = 30/k`. k=3 → 10.0; k=4 → 7.5; k=5 → 6.0; k=6 → 5.0.
- The default for topology maps is 5/0.5625 = 8.89, i.e. 3.375 screen px per art px.
- Clamps were computed for the default size, so a zoomed view pans less far than geometry would allow. That is acceptable.

---

## 6. Q5: harness skeleton (draft, not written to disk)

```csharp
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using CatMiners.Core.Outpost;      // OutpostEvent, OutpostJson (OutpostJson.cs:45), UpgradeBranch, BuyMode
using CatMiners.Core.Save;         // IFileSystem, RealFileSystem (SaveService.cs:15-59)
using CatMiners.Core.Sim;          // ManualClock (Clock.cs:21), AudioGroup
using CatMiners.Runtime;           // GameBootstrap, CameraRig, UiSound, PixelArt
using CatMiners.Runtime.Outpost;   // OutpostController, OutpostViewCueArgs
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;       // RenderPipelineManager, AsyncGPUReadback, GraphicsSettings
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace CatMiners.Tests
{
    /// Ad footage. Run: Tools/unity.ps1 -Mode PlayMode -Target Ci -TestFilter AdCaptureTests
    [Explicit("ad footage: build/adcapture/*.rgba|wav|png + Docs/Reports/ads/*.json")]
    public sealed class AdCaptureTests
    {
        string m_SaveDir;
        ManualClock m_Clock;
        OutpostController Game => GameBootstrap.OutpostController;

        /// Autosave every 2 s (OutpostController.cs:22) without fsync (SaveService.cs:50) to stay inside 33 ms.
        sealed class FastFiles : IFileSystem
        {
            readonly RealFileSystem m_Real = new RealFileSystem();
            public bool Exists(string p) => m_Real.Exists(p);
            public byte[] ReadAllBytes(string p) => m_Real.ReadAllBytes(p);
            public void WriteAllBytesDurable(string p, byte[] d) => File.WriteAllBytes(p, d);
            public void Replace(string a, string b, string c) => m_Real.Replace(a, b, c);
            public void Move(string a, string b) => m_Real.Move(a, b);
            public void Copy(string a, string b) => m_Real.Copy(a, b);
            public void Delete(string p) => m_Real.Delete(p);
            public void CreateDirectory(string p) => m_Real.CreateDirectory(p);
        }

        [UnitySetUp]
        public IEnumerator SetUp()   // = OutpostCardsUiTests.cs:25-39 + FileSystemOverride
        {
            m_SaveDir = Path.Combine(Application.temporaryCachePath, "AdCapture", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_SaveDir);
            CameraRig.OverrideAspect = 9f / 16;                      // CameraRig.cs:34
            m_Clock = new ManualClock(1_800_000_000_000L);
            OutpostController.ClockOverride = m_Clock;              // OutpostController.cs:20
            OutpostController.SaveDirectoryOverride = m_SaveDir;    // :19
            OutpostController.FileSystemOverride = new FastFiles(); // :21, read in Init :114
            GameBootstrap.LegacyMode = false;
            yield return SceneManager.LoadSceneAsync("CoreGame");
            yield return null;
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
        }

        [UnityTearDown]
        public IEnumerator TearDown()  // = OutpostCardsUiTests.cs:40-54 + capture statics
        {
            Time.captureDeltaTime = 0;
            Application.targetFrameRate = 60;                        // GameBootstrap.cs:31
            var empty = SceneManager.CreateScene("AdCaptureCleanup-" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            var scene = SceneManager.GetSceneByName("CoreGame");
            if (scene.IsValid()) yield return SceneManager.UnloadSceneAsync(scene);
            CameraRig.OverrideAspect = 0;
            OutpostController.ClockOverride = null;
            OutpostController.SaveDirectoryOverride = null;
            OutpostController.FileSystemOverride = null;
            AudioListener.pause = false;
            if (Directory.Exists(m_SaveDir)) Directory.Delete(m_SaveDir, true);
        }

        IEnumerator WaitPlaying()      // OutpostCardsUiTests.cs:56-60
        {
            for (int f = 0; f < 2000 && Game.Phase != OutpostControllerPhase.Playing; f++) yield return null;
            Assert.That(Game.Phase, Is.EqualTo(OutpostControllerPhase.Playing));
        }

        [UnityTest, Timeout(1_800_000)]   // default UnityTest timeout (180 s) is too short; cf. OutpostGameplayRecordingTests.cs:81
        public IEnumerator Ad1_StrikeCloseUp()
        {
            Game.SwitchProfile(true); yield return WaitPlaying();   // debug: LoadZone/SimSpeed/GrantTestKit (Commands.cs:238-267)
            Game.Tutorial.Skip();                                   // OutpostTutorial.cs:148
            Game.LoadZone(1); yield return null;
            using (var rec = new AdRecorder(Game, m_Clock))
            {
                yield return rec.Prepare(hud: false);
                yield return rec.FastForward(20, speed: 1);          // settle, not recorded
                int id = Game.Map.Targets.Where(t => Game.CanStrike(t.Id)).Max(t => t.Id); // as OutpostStrikeEvidenceTests.cs:163
                Assert.That(Game.TryGetTargetWorldPosition(id, out var w), Is.True);      // Commands.cs:172
                rec.ZoomPixelPerfect(5);
                var from = new Vector2(w.x, w.z + 4f); var to = new Vector2(w.x, w.z + 1.5f);
                rec.BeginShot("ad1_s01_strike");
                const int n = 6 * AdRecorder.Fps;
                for (int f = 0; f < n; f++)
                {
                    rec.FocusEase(from, to, f, n / 2);
                    if (f % 13 == 3 && Game.CanStrike(id)) rec.Tap(id, new Vector3(0f, 0f, 0.35f));
                    if (f == 0) rec.Mark("hook");
                    rec.EndFrame();
                    yield return null;
                }
                yield return rec.EndShot();
            }
        }
        // Further beats: Hud(true) + Game.Hud.ShowBase() (BasePanel.cs:184) + rec.TapUi(buy row "branch1/buy") + Game.Buy(UpgradeBranch.Pickaxes, BuyMode.One) (Commands.cs:13);
        // gate open: FastForward until EventPresented SectorOpened, FocusEase to Board.GateWorldPosition(i); next mine: Game.ContinueToNextMine() (Commands.cs:126).
    }

    public sealed class AdRecorder : IDisposable
    {
        public const int W = 1080, H = 1920, Fps = 30, FrameBytes = W * H * 4;
        static string RawDir => Path.GetFullPath(Path.Combine(PlayModeUtil.ReportsDir(), "..", "..", "build", "adcapture")); // pattern GameplayRecordingTests.cs:186
        static string MetaDir => Path.Combine(PlayModeUtil.ReportsDir(), "ads");                                         // synced to hub
        static readonly Regex s_Debug = new Regex(@"\s*<color=#FF9070>\[DEBUG\]</color>");                               // OutpostHud.cs:60

        readonly OutpostController m_Game; readonly ManualClock m_Clock;
        readonly long m_Utc0; readonly double m_Mono0; double m_T;
        readonly Camera m_Cam; readonly Canvas m_Canvas; readonly CanvasScaler m_Scaler; readonly RenderTexture m_Rt;
        CanvasGroup m_HudGroup; TMP_Text m_Sector; GameplayRecordingTests.AudioTap m_Tap;   // AudioTap: GameplayRecordingTests.cs:268
        float m_DefaultOrtho; float? m_Zoom;
        object m_Rp; System.Reflection.PropertyInfo m_RenderScale; float m_OldScale = -1;
        Action m_Click, m_Open, m_Close, m_Denied;
        readonly Stopwatch m_Pace = Stopwatch.StartNew(); long m_PaceFrame;
        string m_Shot; bool m_On; int m_First, m_Requested, m_Done, m_Errors, m_Late; double m_Dsp0;
        FileStream m_File; Thread m_Writer; BlockingCollection<(byte[] buf, int idx)> m_Queue;
        readonly ConcurrentBag<byte[]> m_Pool = new ConcurrentBag<byte[]>();
        readonly List<object> m_Frames = new List<object>(), m_Events = new List<object>();

        public AdRecorder(OutpostController game, ManualClock clock)
        {
            m_Game = game; m_Clock = clock; m_Utc0 = clock.UtcNowMs; m_Mono0 = clock.MonotonicSeconds;
            m_Cam = game.Rig.Cam;                                                        // CameraRig.cs:35
            m_Canvas = game.Hud.GetComponent<Canvas>(); m_Scaler = game.Hud.GetComponent<CanvasScaler>();
            m_Rt = new RenderTexture(W, H, 24) { filterMode = FilterMode.Point };       // as Capture(), OutpostEnglishUiEvidenceTests.cs:58
        }

        public IEnumerator Prepare(bool hud)
        {
            Assert.That(SystemInfo.supportsAsyncGPUReadback, Is.True);
            Time.captureDeltaTime = 1f / Fps;            // scaled time exact; unscaled is NOT (OutpostStrikeEvidenceTests.cs:169)
            Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0;          // pacing is ours (PerformanceTests.cs:39-47)
            var rp = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;
            m_Rp = rp; m_RenderScale = rp != null ? rp.GetType().GetProperty("renderScale") : null;    // Mobile_RPAsset.asset:29 = 0.8
            if (m_RenderScale != null) { m_OldScale = (float)m_RenderScale.GetValue(rp); m_RenderScale.SetValue(rp, 1f); }
            UnityEngine.Random.InitState(20261013);
            m_Game.SetVolume(AudioGroup.Master, 1);                                      // Commands.cs:207
            m_Cam.targetTexture = m_Rt;
            m_Scaler.enabled = false; m_Canvas.scaleFactor = 1f;
            m_Canvas.renderMode = RenderMode.ScreenSpaceCamera; m_Canvas.worldCamera = m_Cam; m_Canvas.planeDistance = 1;
            m_HudGroup = m_Game.Hud.GetComponent<CanvasGroup>();
            if (m_HudGroup == null) m_HudGroup = m_Game.Hud.gameObject.AddComponent<CanvasGroup>();
            Hud(hud); HideNode("SafeArea/Settings"); HideNode("SafeArea/TutorialSkip"); // OutpostHud.cs:407,415
            m_Game.Hud.CloseAllWindows(); m_Game.ClearSelection();
            m_Tap = m_Cam.gameObject.AddComponent<GameplayRecordingTests.AudioTap>();   // AudioListener is on Main Camera (CoreGame.unity:318-325)
            m_Game.EventPresented += OnCore;                                             // OutpostController.cs:57, raised Loop.cs:117
            m_Game.Board.Cue += OnCue;                                                   // OutpostBoardView.cs:82
            m_Click = UiSound.Click; m_Open = UiSound.Open; m_Close = UiSound.Close; m_Denied = UiSound.Denied; // UiSound.cs:12-18
            UiSound.Click = () => { Log("ui", "click"); m_Click?.Invoke(); };
            UiSound.Open = () => { Log("ui", "open"); m_Open?.Invoke(); };
            UiSound.Close = () => { Log("ui", "close"); m_Close?.Invoke(); };
            UiSound.Denied = () => { Log("ui", "denied"); m_Denied?.Invoke(); };
            RenderPipelineManager.endContextRendering += OnRendered;
            for (int i = 0; i < 15; i++) { EndFrame(); yield return null; }             // insets → SetUiInsets → Refit
            m_Game.Hud.RefreshNow(); Canvas.ForceUpdateCanvases();                       // OutpostHud.cs:912
            m_DefaultOrtho = m_Cam.orthographicSize;
            ResyncPace();
        }

        void Advance(double s)   // absolute: no UTC/mono drift (Clock.cs:23-24 setters)
        {
            m_T += s;
            m_Clock.MonotonicSeconds = m_Mono0 + m_T;
            m_Clock.UtcNowMs = m_Utc0 + (long)Math.Round(m_T * 1000.0);
        }

        /// Not recorded, not paced; <=1 s per frame or the controller goes offline (Loop.cs:71).
        public IEnumerator FastForward(double simSeconds, int speed = 100)
        {
            m_Game.SimSpeed = speed;                                                     // debug only: 1/5/20/100 (OutpostController.cs:66-77)
            for (double t = 0; t < simSeconds / Math.Max(1, m_Game.SimSpeed); t += 0.25) { Advance(0.25); yield return null; }
            m_Game.SimSpeed = 1;
            ResyncPace();
        }

        /// Call once per iteration, immediately before `yield return null`.
        public void EndFrame()
        {
            if (m_Zoom.HasValue && !Mathf.Approximately(m_Cam.orthographicSize, m_Zoom.Value)) { m_Cam.orthographicSize = m_Zoom.Value; Refocus(); }
            StripDebugMarker();
            if (m_On) m_Frames.Add(new { i = Time.frameCount - m_First, dt = Time.unscaledDeltaTime, dsp = AudioSettings.dspTime,
                simUs = m_Game.Sim != null ? m_Game.Sim.Now : -1, fx = m_Game.Rig.FocusX, fz = m_Game.Rig.FocusZ, ortho = m_Cam.orthographicSize });
            Advance(1.0 / Fps);
            if (Pace() && m_On) m_Late++;
        }

        bool Pace()   // PerformanceTests.cs:328-340 at 30 fps; true = frame overran
        {
            m_PaceFrame++;
            long due = m_PaceFrame * Stopwatch.Frequency / Fps, now = m_Pace.ElapsedTicks;
            if (now >= due) { m_PaceFrame = now * Fps / Stopwatch.Frequency; return true; }
            while (m_Pace.ElapsedTicks < due) { }
            return false;
        }
        void ResyncPace() => m_PaceFrame = m_Pace.ElapsedTicks * Fps / Stopwatch.Frequency;

        public void BeginShot(string name)
        {
            m_Shot = name; m_Frames.Clear(); m_Events.Clear(); m_Requested = m_Done = m_Errors = m_Late = 0;
            Directory.CreateDirectory(RawDir); Directory.CreateDirectory(MetaDir);
            m_File = new FileStream(Path.Combine(RawDir, name + ".rgba"), FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20);
            m_Queue = new BlockingCollection<(byte[], int)>(24);
            m_Writer = new Thread(() =>
            {
                foreach (var (buf, idx) in m_Queue.GetConsumingEnumerable())
                { m_File.Position = (long)idx * FrameBytes; m_File.Write(buf, 0, FrameBytes); m_Pool.Add(buf); }
            }) { IsBackground = true, Name = "AdCaptureWriter" };
            m_Writer.Start();
            m_First = Time.frameCount;                 // this frame's render = image 0
            m_Dsp0 = AudioSettings.dspTime; m_Tap.Begin();
            m_On = true;
        }

        void OnRendered(ScriptableRenderContext ctx, List<Camera> cameras)   // after LateUpdate: exact current frame
        {
            if (!m_On || !cameras.Contains(m_Cam)) return;
            int idx = Time.frameCount - m_First; m_Requested++;
            AsyncGPUReadback.Request(m_Rt, 0, TextureFormat.RGBA32, r =>
            {
                if (r.hasError) { m_Errors++; return; }
                if (!m_Pool.TryTake(out var buf)) buf = new byte[FrameBytes];
                r.GetData<byte>().CopyTo(buf);
                m_Queue.Add((buf, idx)); m_Done++;
            });
        }

        public IEnumerator EndShot()
        {
            m_On = false;
            int frames = Time.frameCount - m_First;
            SaveLastPng(Path.Combine(RawDir, m_Shot + "_last.png"));   // RT still holds the last recorded image
            m_Tap.End();
            AsyncGPUReadback.WaitAllRequests();
            for (int g = 0; g < 300 && m_Done + m_Errors < m_Requested; g++) yield return null;
            m_Queue.CompleteAdding(); m_Writer.Join(); m_File.Dispose();
            float[] s = m_Tap.Samples(); int ch = m_Tap.Channels, sr = AudioSettings.outputSampleRate;
            if (ch > 0) WriteWav(Path.Combine(RawDir, m_Shot + ".wav"), s, ch, sr);
            File.WriteAllText(Path.Combine(MetaDir, m_Shot + ".json"), OutpostJson.Serialize(new {
                shot = m_Shot, width = W, height = H, fps = Fps, pixFmt = "rgba", rows = "bottom-up (ffmpeg -vf vflip; verify vs _last.png)",
                frames, requested = m_Requested, done = m_Done, readbackErrors = m_Errors, lateFrames = m_Late,
                audio = new { channels = ch, sampleRate = sr, dspStart = m_Dsp0 },
                unity = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName,
                quality = QualitySettings.names[QualitySettings.GetQualityLevel()], renderScaleWas = m_OldScale,
                zone = m_Game.State.Run.Zone, seed = m_Game.State.Profile.Seed, balance = m_Game.Configs.Balance.Version, // as OutpostGameplayRecordingTests.cs:213-214
                frameLog = m_Frames, events = m_Events }) + "\n");
            Assert.That(m_Errors, Is.Zero, "readback errors");
            Assert.That(m_Done, Is.EqualTo(frames), "frames written");
            ResyncPace();
        }

        // ---- sidecar events (image index f = Time.frameCount - m_First; events in Update F are visible in image F) ----
        void Log(string kind, string name, object data = null) { if (m_On) m_Events.Add(new { f = Time.frameCount - m_First, kind, name, data }); }
        Vector2 ToImage(Vector3 world) { var p = m_Cam.WorldToScreenPoint(world); return new Vector2(p.x, H - p.y); }   // top-left origin
        void OnCore(OutpostEvent e) => Log("core", e.Type.ToString(),                                                   // OutpostEvents.cs:39-57
            new { e.TargetId, e.WorkerId, e.Count, e.Branch, e.Slot, value = e.Value.ToString(), e.ItemId });
        void OnCue(OutpostViewCueArgs c) { var p = ToImage(c.Position);                                                  // OutpostViewCue.cs:44-62
            Log("sfx", c.Cue.ToString(), new { x = p.x, y = p.y, material = c.Material.ToString(), c.Slot, c.Branch, c.Count, value = c.Value.ToString() }); }
        public void Mark(string name) => Log("mark", name);

        public bool Tap(int targetId, Vector3 offset = default)
        {
            if (!m_Game.TryGetTargetWorldPosition(targetId, out var world)) return false;  // Commands.cs:172
            world += offset; var p = ToImage(world);
            bool ok = m_Game.BeginStrike(targetId, world);                                  // Commands.cs:32 (0.4 s tap interval on ManualClock, :21)
            Log("tap", "strike", new { targetId, x = p.x, y = p.y, ok });
            return ok;
        }
        public void TapUi(Button b)
        {
            var rt = (RectTransform)b.transform;
            var p = RectTransformUtility.WorldToScreenPoint(m_Cam, rt.TransformPoint(rt.rect.center));
            Log("tap", "ui:" + b.name, new { x = p.x, y = H - p.y });
            PlayModeUtil.Click(b);                                                          // PlayModeUtil.cs:78-82
        }

        // ---- camera / HUD ----
        public void Focus(Vector2 xz) => m_Game.Rig.FocusOn(xz);                          // CameraRig.cs:329-336
        public void FocusEase(Vector2 a, Vector2 b, int frame, int frames)
        { float t = Mathf.Clamp01(frame / (float)Mathf.Max(1, frames - 1)); Focus(Vector2.Lerp(a, b, t * t * (3f - 2f * t))); }
        public void ZoomPixelPerfect(int screenPxPerArtPx) => Zoom(H / (2f * PixelArt.CellPixels * screenPxPerArtPx)); // 30/k; PixelArt.cs:40
        public void Zoom(float? ortho) { m_Zoom = ortho; m_Cam.orthographicSize = ortho ?? m_DefaultOrtho; Refocus(); }
        void Refocus() => m_Game.Rig.FocusOn(new Vector2(m_Game.Rig.FocusX, m_Game.Rig.FocusZ));  // re-Apply (CameraRig.cs:426-438)
        public void Hud(bool on) => m_HudGroup.alpha = on ? 1f : 0f;
        void HideNode(string path)
        {
            var t = m_Game.Hud.transform.Find(path); if (t == null) return;
            var g = t.GetComponent<CanvasGroup>(); if (g == null) g = t.gameObject.AddComponent<CanvasGroup>(); g.alpha = 0f;
        }
        void StripDebugMarker()
        {
            if (m_Sector == null) m_Sector = m_Game.Hud.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "sector"); // OutpostHud.cs:360
            if (m_Sector != null && m_Sector.text.Contains("[DEBUG]")) m_Sector.text = s_Debug.Replace(m_Sector.text, "");
        }

        void SaveLastPng(string path)
        {
            var prev = RenderTexture.active; RenderTexture.active = m_Rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(false);
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG()); UnityEngine.Object.Destroy(tex);
        }
        static void WriteWav(string path, float[] s, int ch, int sr)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = s.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16); w.Write((short)1); w.Write((short)ch); w.Write(sr); w.Write(sr * ch * 2); w.Write((short)(ch * 2)); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                foreach (float v in s) w.Write((short)Mathf.RoundToInt(Mathf.Clamp(v, -1f, 1f) * 32767f));
            }
        }

        public void Dispose()
        {
            m_On = false;
            RenderPipelineManager.endContextRendering -= OnRendered;
            if (m_Game != null) { m_Game.EventPresented -= OnCore; if (m_Game.Board != null) m_Game.Board.Cue -= OnCue; }
            UiSound.Click = m_Click; UiSound.Open = m_Open; UiSound.Close = m_Close; UiSound.Denied = m_Denied;
            if (m_RenderScale != null && m_OldScale > 0) m_RenderScale.SetValue(m_Rp, m_OldScale);
            Time.captureDeltaTime = 0;
            if (m_Cam != null) m_Cam.targetTexture = null;
            if (m_Canvas != null) { m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay; m_Canvas.worldCamera = null; }
            if (m_Scaler != null) m_Scaler.enabled = true;
            if (m_HudGroup != null) m_HudGroup.alpha = 1f;
            if (m_Tap != null) UnityEngine.Object.Destroy(m_Tap);
            m_Rt.Release(); UnityEngine.Object.Destroy(m_Rt);
        }
    }
}
```

Notes on the skeleton:
- The `using` block runs `Dispose` even if the test fails.
- If `endContextRendering` turns out unreliable, fall back to issuing the readback right after `yield return null`. The image is then the previous Unity frame; map it as `idx = Time.frameCount - 1 - m_First`.
- An optional extra event source: poll each `AudioSource` (`isPlaying` combined with a clip change or `timeSamples` going down) to log exact clip variants and pitches. `Sfx` picks a random variant and pitch (Sfx.cs:269-276); volumes are in the table at Sfx.cs:89-128. The `.ogg` source clips exist only on the PC (`Assets/Game/Resources/Audio/*.ogg`; only `.meta` files are on the hub).

### Commands

Run from the hub, project root `/mnt/claude-data/sargsyan-tinote/cats-cute-miner`:
```bash
python3 Tools/gen_meta.py                                   # .meta for the new test file
on-remote powershell -ExecutionPolicy Bypass -File Tools/unity.ps1 -Mode Compile -Target Ci            # optional pre-check
on-remote powershell -ExecutionPolicy Bypass -File Tools/unity.ps1 -Mode PlayMode -Target Ci -TestFilter AdCaptureTests
#   single shot: -TestFilter "AdCaptureTests.Ad1_StrikeCloseUp"
#   Run with Bash run_in_background (can exceed 10 min); do NOT wrap in `timeout` (breaks on-remote routing).
on-remote python -m venv build/adtools/venv
on-remote powershell -NoProfile -Command "build\adtools\venv\Scripts\python.exe -m pip install imageio-ffmpeg"
on-remote powershell -NoProfile -Command "\$ff = build\adtools\venv\Scripts\python.exe -c 'import imageio_ffmpeg as m; print(m.get_ffmpeg_exe())'; & \$ff -hide_banner -y -f rawvideo -pix_fmt rgba -s 1080x1920 -r 30 -i build\adcapture\ad1_s01_strike.rgba -vf vflip -c:v libx264 -preset slow -crf 12 -pix_fmt yuv444p -g 30 -movflags +faststart build\adcapture\ad1_s01_strike.mp4"
on-remote --fetch build/adcapture/ad1_s01_strike.mp4 build/adcapture/ad1_s01_strike.wav build/adcapture/ad1_s01_strike_last.png
```
- The sidecar `Docs/Reports/ads/ad1_s01_strike.json` arrives on the hub by itself.
- Directly on the PC: `powershell -ExecutionPolicy Bypass -File Tools\unity.ps1 -Mode PlayMode -Target Ci -TestFilter AdCaptureTests`.
- The Real target only works with the Editor closed.

---

## 7. Acceptance checks and risks

1. **Timing:** in `frameLog`, `|dt − 0.0333| < 3 ms` for ≥99% of frames and `lateFrames == 0`, otherwise re-shoot. Spike sources are autosave (mitigated by `FastFiles`), first-use shader compilation (mitigated by warm-up before `BeginShot`) and GC (incremental GC is on, ProjectSettings.asset:845).
2. **Orientation and colour:** the last raw frame, vflipped, must equal `_last.png` byte for byte in RGB. This confirms the AsyncGPUReadback row order and sRGB handling.
3. **Audio length:** WAV duration ≈ frames/30 ± 50 ms. Align audio with `dspStart` and the per-frame `dsp` values.
4. **Render scale:** `renderScaleWas` and `quality` are in the sidecar. Mobile at 0.8 with bilinear upscaling would soften the pixels, so force 1.0.
5. **Static leaks:** `captureDeltaTime`, `targetFrameRate`, `FileSystemOverride` and `UiSound` delegates must be restored (TearDown and Dispose).
6. **Ad hygiene:** no `[DEBUG]` tag, no Settings or TutorialSkip buttons, tutorial skipped. The DEV panel is absent in tests (tests-tools.md:431).
7. **Disk:** about 2.5 GB of raw data per 10 seconds. Encode, then delete the `.rgba` per shot. Never write raw files outside `build/`, because Mutagen would sync them.
8. **GPU:** batchmode uses the Radeon iGPU (log :116). That is adequate. Optionally set Unity.exe to "High performance" in Windows graphics settings to use the dGPU.